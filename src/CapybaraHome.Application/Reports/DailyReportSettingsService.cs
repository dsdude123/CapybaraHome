using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Reports;

/// <summary>
/// Reads and writes the one row that says how the report is built and printed. A new installation
/// starts with the schedule off and nothing filled in, and everything about the report is set on
/// the settings page.
/// </summary>
public class DailyReportSettingsService(ICapybaraHomeDbContext context)
{
    /// <summary>Longest a printer's host name or queue is allowed to be.</summary>
    private const int LongestName = 500;

    /// <summary>Returns the settings, creating them from the host's configuration the first time.</summary>
    public async Task<DailyReportSettingsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        var calendars = await ReadCalendarsAsync(cancellationToken);
        var categories = await context.ReportCategories
            .AsNoTracking()
            .Select(c => c.CategoryId)
            .ToListAsync(cancellationToken);

        return new DailyReportSettingsView(
            settings.IsEnabled,
            settings.TimeOfDay,
            settings.PrinterHost,
            settings.PrinterPort,
            settings.PrinterQueue,
            settings.PrintBothSides,
            settings.WeatherUrl,
            [.. categories.Order()],
            calendars,
            settings.LastRunAt,
            settings.LastRunError);
    }

    /// <summary>Returns the settings row itself, creating it the first time it is asked for.</summary>
    public async Task<DailyReportSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await context.DailyReportSettings
            .SingleOrDefaultAsync(s => s.Id == DailyReportSettings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new DailyReportSettings();
        context.DailyReportSettings.Add(settings);
        await context.SaveChangesAsync(cancellationToken);

        return settings;
    }

    /// <summary>Returns the calendars the report reads, by kind and then by their own order.</summary>
    public async Task<IReadOnlyList<ReportCalendarView>> ReadCalendarsAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);

        var calendars = await context.ReportCalendars
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // The kind is stored as its name, so the sections are put in their own order here rather
        // than alphabetically by the database.
        return
        [
            .. calendars
                .OrderBy(c => (int)c.Kind)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .Select(c => new ReportCalendarView(c.Id, c.Name, c.Url, c.Kind, c.IsEnabled, c.SortOrder)),
        ];
    }

    /// <summary>Writes the schedule, the printer, the forecast address and the tracked categories.</summary>
    /// <exception cref="ArgumentException">A field is outside what the report can be given.</exception>
    public async Task<DailyReportSettingsView> SaveAsync(
        UpdateDailyReportSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        settings.IsEnabled = request.IsEnabled;
        settings.TimeOfDay = request.TimeOfDay;
        settings.PrinterHost = Host(request.PrinterHost);
        settings.PrinterPort = Port(request.PrinterPort);
        settings.PrinterQueue = Queue(request.PrinterQueue);
        settings.PrintBothSides = request.PrintBothSides;
        settings.WeatherUrl = Address(request.WeatherUrl);

        await SaveCategoriesAsync(request.CategoryIds ?? [], cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(cancellationToken);
    }

    /// <summary>Adds a calendar, at the end of the ones of its kind.</summary>
    /// <exception cref="ArgumentException">The name or the address is missing or malformed.</exception>
    public async Task<ReportCalendarView> AddCalendarAsync(ReportCalendarInput input, CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);

        var name = Name(input.Name);
        var url = Address(input.Url) ?? throw new ArgumentException("A calendar needs an address.");

        var last = await context.ReportCalendars
            .Where(c => c.Kind == input.Kind)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync(cancellationToken);

        var calendar = new ReportCalendar
        {
            Name = name,
            Url = url,
            Kind = input.Kind,
            IsEnabled = input.IsEnabled,
            SortOrder = (last ?? -1) + 1,
        };

        context.ReportCalendars.Add(calendar);
        await context.SaveChangesAsync(cancellationToken);

        return new ReportCalendarView(calendar.Id, calendar.Name, calendar.Url, calendar.Kind, calendar.IsEnabled, calendar.SortOrder);
    }

    /// <summary>Changes a calendar, or returns null when no calendar has that id.</summary>
    /// <exception cref="ArgumentException">The name or the address is missing or malformed.</exception>
    public async Task<ReportCalendarView?> UpdateCalendarAsync(
        int id,
        ReportCalendarInput input,
        CancellationToken cancellationToken = default)
    {
        var calendar = await context.ReportCalendars.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calendar is null)
        {
            return null;
        }

        calendar.Name = Name(input.Name);
        calendar.Url = Address(input.Url) ?? throw new ArgumentException("A calendar needs an address.");
        calendar.Kind = input.Kind;
        calendar.IsEnabled = input.IsEnabled;

        await context.SaveChangesAsync(cancellationToken);

        return new ReportCalendarView(calendar.Id, calendar.Name, calendar.Url, calendar.Kind, calendar.IsEnabled, calendar.SortOrder);
    }

    /// <summary>Removes a calendar, or returns false when no calendar has that id.</summary>
    public async Task<bool> DeleteCalendarAsync(int id, CancellationToken cancellationToken = default)
    {
        var calendar = await context.ReportCalendars.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calendar is null)
        {
            return false;
        }

        context.ReportCalendars.Remove(calendar);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Records that the schedule printed the report, or why it could not.</summary>
    public async Task RecordRunAsync(DateTimeOffset at, string? error, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);

        settings.LastRunAt = at;
        settings.LastRunError = error is { Length: > 0 } message
            ? message[..Math.Min(message.Length, 2000)]
            : null;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Returns the ids of the categories the task table is kept to, empty for every category.</summary>
    public async Task<IReadOnlyList<int>> TrackedCategoryIdsAsync(CancellationToken cancellationToken = default)
    {
        return await context.ReportCategories
            .AsNoTracking()
            .Select(c => c.CategoryId)
            .ToListAsync(cancellationToken);
    }

    private async Task SaveCategoriesAsync(IReadOnlyCollection<int> wanted, CancellationToken cancellationToken)
    {
        var ids = wanted.Distinct().ToList();

        if (ids.Count > 0)
        {
            var known = await context.Categories
                .Where(c => ids.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            var missing = ids.Except(known).ToList();
            if (missing.Count > 0)
            {
                throw new ArgumentException($"No category has id {missing[0]}.");
            }
        }

        var current = await context.ReportCategories.ToListAsync(cancellationToken);

        foreach (var row in current.Where(row => !ids.Contains(row.CategoryId)))
        {
            context.ReportCategories.Remove(row);
        }

        foreach (var id in ids.Where(id => current.All(row => row.CategoryId != id)))
        {
            context.ReportCategories.Add(new ReportCategory { CategoryId = id });
        }
    }

    private static string Name(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        return name.Length > 0
            ? name[..Math.Min(name.Length, 200)]
            : throw new ArgumentException("A calendar needs a name.");
    }

    private static string? Host(string? value)
    {
        var host = Blank(value);
        if (host is null)
        {
            return null;
        }

        if (host.Length > LongestName || host.Contains('/') || host.Contains(' ') || host.Contains(':'))
        {
            throw new ArgumentException("A printer is named by its host name alone, such as printer.lan.");
        }

        return host;
    }

    private static int Port(int value) => value is > 0 and <= 65535
        ? value
        : throw new ArgumentException("A port is between 1 and 65535.");

    private static string Queue(string? value)
    {
        var queue = Blank(value) ?? "/ipp/print";
        if (queue.Length > LongestName)
        {
            throw new ArgumentException("That queue path is too long.");
        }

        return queue.StartsWith('/') ? queue : "/" + queue;
    }

    /// <summary>
    /// Reads an address the server itself will fetch. Only http and https, and never one carrying
    /// a password: the addresses here are a home network's own services, so a private one is the
    /// normal case rather than something to refuse.
    /// </summary>
    private static string? Address(string? value)
    {
        var address = Blank(value);
        if (address is null)
        {
            return null;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An address starts with http:// or https://.");
        }

        if (uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("An address must not carry a username or a password.");
        }

        return uri.ToString();
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
