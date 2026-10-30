using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Reports;
using CapybaraHome.Domain.Tasks;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Application.Reports;

/// <summary>
/// Gathers one morning's report, as the settings page says it should be built. Every source is
/// read on its own and a source that fails leaves its own section empty rather than losing the
/// report, because a printed page that is short one table is still worth having at breakfast.
/// </summary>
public class DailyReportService(
    TaskService tasks,
    MealService meals,
    WeatherFeed weather,
    CalendarFeed calendars,
    NewsFeed news,
    DailyReportSettingsService settings,
    TimeProvider clock,
    ILogger<DailyReportService> logger)
{
    /// <summary>The national wire.</summary>
    private const string NationalNewsUrl = "https://feedx.net/rss/ap.xml";

    /// <summary>The local station's news page.</summary>
    private const string LocalNewsUrl = "https://www.king5.com/local";

    /// <summary>How far ahead the task table reaches.</summary>
    private const int TaskWindowDays = 7;

    /// <summary>How far ahead the events and holidays reach.</summary>
    private const int CalendarWindowDays = 30;

    /// <summary>How many headlines each news source contributes.</summary>
    private const int HeadlineCount = 5;

    /// <summary>The time zone the report's day and times are read in.</summary>
    public TimeZoneInfo Zone => clock.LocalTimeZone;

    /// <summary>Builds the report for today.</summary>
    public async Task<DailyReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);
        var configured = await settings.LoadAsync(cancellationToken);
        var feeds = await settings.ReadCalendarsAsync(cancellationToken);
        var tracked = await settings.TrackedCategoryIdsAsync(cancellationToken);

        return new DailyReport(
            today,
            await ReadAsync("weather", () => ReadWeatherAsync(configured, cancellationToken)),
            await ReadAsync("tasks", () => ReadTasksAsync(today, tracked, cancellationToken)),
            await ReadAsync("meals", () => ReadMealsAsync(cancellationToken)),
            await ReadEventsAsync(feeds, today, cancellationToken),
            await ReadHolidaysAsync(feeds, today, cancellationToken),
            await ReadTodayAsync(feeds, today, cancellationToken),
            await ReadAsync("local news", () => news.ReadPageAsync(LocalNewsUrl, HeadlineCount, cancellationToken)),
            await ReadAsync("national news", () => news.ReadFeedAsync(NationalNewsUrl, HeadlineCount, cancellationToken)));
    }

    /// <summary>Reads one section, or logs why it is missing and leaves it empty.</summary>
    private async Task<IReadOnlyList<T>> ReadAsync<T>(string section, Func<Task<IReadOnlyList<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The {Section} section of the daily report could not be read.", section);
            return [];
        }
    }

    private async Task<IReadOnlyList<WeatherPeriod>> ReadWeatherAsync(DailyReportSettings configured, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configured.WeatherUrl))
        {
            return [];
        }

        return await weather.ReadAsync(configured.WeatherUrl, cancellationToken);
    }

    /// <summary>
    /// Reads the open tasks due within the week. The tracked categories are applied here rather
    /// than in the query because the listing is a week long at most, and narrowing it afterwards
    /// leaves the task filters as they are for every other screen.
    /// </summary>
    private async Task<IReadOnlyList<ReportTask>> ReadTasksAsync(
        DateOnly today,
        IReadOnlyList<int> tracked,
        CancellationToken cancellationToken)
    {
        var open = await tasks.ListAsync(
            new TaskFilter(
                Statuses: [TaskItemStatus.ToDo, TaskItemStatus.InProgress, TaskItemStatus.OnHold],
                DueWithinDays: TaskWindowDays),
            TaskSort.DueDate,
            cancellationToken);

        if (tracked.Count > 0)
        {
            open = [.. open.Where(task => task.CategoryId is { } id && tracked.Contains(id))];
        }

        return [.. open.Select(task => new ReportTask(
            task.Id,
            task.Title,
            task.DueDate,
            task.DueDate is { } due && due < today,
            task.DueDate == today))];
    }

    private async Task<IReadOnlyList<ReportMeal>> ReadMealsAsync(CancellationToken cancellationToken)
    {
        var planned = await meals.ListAsync(
            new MealFilter(Statuses: [MealStatus.Planned, MealStatus.PlannedShared]),
            MealSort.Name,
            cancellationToken);

        return [.. planned.Select(meal => new ReportMeal(
            meal.Id,
            meal.PlannedVariantName is { Length: > 0 } variant ? $"{meal.Name} ({variant})" : meal.Name,
            meal.Status))];
    }

    private async Task<IReadOnlyList<ReportEvent>> ReadEventsAsync(
        IReadOnlyList<ReportCalendarView> feeds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var entries = new List<ReportEvent>();

        foreach (var feed in Enabled(feeds, ReportCalendarKind.Upcoming))
        {
            entries.AddRange(await ReadAsync(
                feed.Name,
                () => calendars.ReadAsync(feed.Url, today, today.AddDays(CalendarWindowDays), cancellationToken)));
        }

        return Ordered(entries);
    }

    private async Task<IReadOnlyList<ReportHoliday>> ReadHolidaysAsync(
        IReadOnlyList<ReportCalendarView> feeds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var entries = new List<ReportEvent>();

        foreach (var feed in Enabled(feeds, ReportCalendarKind.Holidays))
        {
            entries.AddRange(await ReadAsync(
                feed.Name,
                () => calendars.ReadAsync(feed.Url, today, today.AddDays(CalendarWindowDays), cancellationToken)));
        }

        return [.. Ordered(entries).Select(entry => new ReportHoliday(entry.Summary, entry.Date))];
    }

    private async Task<IReadOnlyList<ReportTodaySection>> ReadTodayAsync(
        IReadOnlyList<ReportCalendarView> feeds,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var sections = new List<ReportTodaySection>();

        foreach (var feed in Enabled(feeds, ReportCalendarKind.Today))
        {
            var entries = await ReadAsync(
                feed.Name,
                () => calendars.ReadDayAsync(feed.Url, today, cancellationToken));

            if (entries.Count > 0)
            {
                sections.Add(new ReportTodaySection(feed.Name, entries));
            }
        }

        return sections;
    }

    private static IEnumerable<ReportCalendarView> Enabled(IReadOnlyList<ReportCalendarView> feeds, ReportCalendarKind kind) =>
        feeds.Where(feed => feed.IsEnabled && feed.Kind == kind);

    private static IReadOnlyList<ReportEvent> Ordered(List<ReportEvent> entries) =>
        [.. entries
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Time ?? TimeOnly.MinValue)
            .ThenBy(e => e.Summary, StringComparer.Ordinal)];
}
