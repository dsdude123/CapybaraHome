using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.Reports;

/// <summary>
/// Gathers one morning's report. Every source is read on its own and a source that fails leaves
/// its own section empty rather than losing the report, because a printed page that is short one
/// table is still worth having at breakfast.
/// </summary>
public class DailyReportService(
    TaskService tasks,
    MealService meals,
    WeatherFeed weather,
    CalendarFeed calendars,
    NewsFeed news,
    IOptions<DailyReportOptions> options,
    TimeProvider clock,
    ILogger<DailyReportService> logger)
{
    /// <summary>Public holidays, as Google publishes them for the United States.</summary>
    private const string HolidayCalendarUrl =
        "https://calendar.google.com/calendar/ical/en.usa%23holiday%40group.v.calendar.google.com/public/basic.ics";

    /// <summary>The baseball season, as Google publishes it.</summary>
    private const string SportsCalendarUrl =
        "https://calendar.google.com/calendar/ical/mlb_-m-06wpc_Seattle%2BMariners%23sports%40group.v.calendar.google.com/public/basic.ics";

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
        var settings = options.Value;

        return new DailyReport(
            today,
            await ReadAsync("weather", () => ReadWeatherAsync(settings, cancellationToken)),
            await ReadAsync("tasks", () => ReadTasksAsync(today, cancellationToken)),
            await ReadAsync("meals", () => ReadMealsAsync(cancellationToken)),
            await ReadAsync("calendar", () => ReadEventsAsync(settings, today, cancellationToken)),
            await ReadAsync("holidays", () => ReadHolidaysAsync(today, cancellationToken)),
            await ReadAsync("games", () => calendars.ReadDayAsync(SportsCalendarUrl, today, cancellationToken)),
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

    private async Task<IReadOnlyList<WeatherPeriod>> ReadWeatherAsync(DailyReportOptions settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.WeatherUrl))
        {
            return [];
        }

        return await weather.ReadAsync(settings.WeatherUrl, cancellationToken);
    }

    private async Task<IReadOnlyList<ReportTask>> ReadTasksAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var open = await tasks.ListAsync(
            new TaskFilter(
                Statuses: [TaskItemStatus.ToDo, TaskItemStatus.InProgress, TaskItemStatus.OnHold],
                DueWithinDays: TaskWindowDays),
            TaskSort.DueDate,
            cancellationToken);

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

    private async Task<IReadOnlyList<ReportEvent>> ReadEventsAsync(DailyReportOptions settings, DateOnly today, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CalendarUrl))
        {
            return [];
        }

        return await calendars.ReadAsync(settings.CalendarUrl, today, today.AddDays(CalendarWindowDays), cancellationToken);
    }

    private async Task<IReadOnlyList<ReportHoliday>> ReadHolidaysAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var entries = await calendars.ReadAsync(HolidayCalendarUrl, today, today.AddDays(CalendarWindowDays), cancellationToken);
        return [.. entries.Select(entry => new ReportHoliday(entry.Summary, entry.Date))];
    }
}
