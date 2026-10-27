using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace CapybaraHome.Application.Reports;

/// <summary>Reads an iCalendar feed and expands its recurrences over a window of days.</summary>
public class CalendarFeed(HttpClient client, TimeProvider clock)
{
    /// <summary>
    /// Returns the entries of <paramref name="url"/> that fall between <paramref name="from"/> and
    /// <paramref name="toExclusive"/>, as days in the server's own time zone, earliest first. An
    /// entry that takes the whole day carries no time.
    /// </summary>
    public async Task<IReadOnlyList<ReportEvent>> ReadAsync(
        string url,
        DateOnly from,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        var text = await client.GetStringAsync(url, cancellationToken);
        var zone = clock.LocalTimeZone;
        var calendars = CalendarCollection.Load(text);

        var events = calendars
            .GetOccurrences(Bound(from, zone))
            .TakeWhileBefore(Bound(toExclusive, zone))
            .Select(occurrence => Describe(occurrence, zone))
            .ToList();

        return [.. events
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Time ?? TimeOnly.MinValue)
            .ThenBy(e => e.Summary, StringComparer.Ordinal)];
    }

    /// <summary>Returns the entries of <paramref name="url"/> that fall on <paramref name="day"/>.</summary>
    public Task<IReadOnlyList<ReportEvent>> ReadDayAsync(string url, DateOnly day, CancellationToken cancellationToken = default) =>
        ReadAsync(url, day, day.AddDays(1), cancellationToken);

    /// <summary>Turns a local midnight into the instant the expansion is bounded by.</summary>
    private static CalDateTime Bound(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var instant = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return new CalDateTime(instant.UtcDateTime, "UTC", true);
    }

    /// <summary>
    /// Reads one occurrence as the day and time it happens locally. A start with no zone of its own
    /// is a wall time wherever it is read, so it is taken as written rather than converted.
    /// </summary>
    private static ReportEvent Describe(Occurrence occurrence, TimeZoneInfo zone)
    {
        var source = occurrence.Source as CalendarEvent;
        var start = occurrence.Period.StartTime;
        var summary = source?.Summary ?? string.Empty;
        var location = source?.Location ?? string.Empty;

        if (!start.HasTime)
        {
            return new ReportEvent(summary, start.Date, null, location);
        }

        var local = start.IsFloating
            ? start.Value
            : TimeZoneInfo.ConvertTimeFromUtc(start.AsUtc, zone);

        return new ReportEvent(summary, DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), location);
    }
}
