using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Reports;
using CapybaraHome.Domain.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>Covers what the report says about this app's own tasks and meals, in place of the board it replaced.</summary>
public class DailyReportServiceTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    private const string EmptyCalendar = """
    BEGIN:VCALENDAR
    VERSION:2.0
    PRODID:-//tests//EN
    END:VCALENDAR
    """;

    [Fact]
    public async Task The_report_lists_the_open_tasks_due_within_a_week_and_marks_the_pressing_ones()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = new TaskService(db.Context, db.Clock);
        var today = Today(db.Clock);

        await tasks.CreateAsync(new CreateTaskRequest("Overdue", DueDate: today.AddDays(-2)));
        await tasks.CreateAsync(new CreateTaskRequest("Due today", DueDate: today));
        await tasks.CreateAsync(new CreateTaskRequest("Due in a week", DueDate: today.AddDays(7)));
        await tasks.CreateAsync(new CreateTaskRequest("Due after the window", DueDate: today.AddDays(8)));
        await tasks.CreateAsync(new CreateTaskRequest("No due date"));
        await tasks.CreateAsync(new CreateTaskRequest("Finished", Status: TaskItemStatus.Done, DueDate: today));

        var report = await Build(db);

        Assert.Equal(["Overdue", "Due today", "Due in a week"], report.Tasks.Select(t => t.Title));
        Assert.True(report.Tasks[0].IsOverdue);
        Assert.False(report.Tasks[0].IsDueToday);
        Assert.True(report.Tasks[1].IsDueToday);
        Assert.False(report.Tasks[2].IsOverdue);
        Assert.False(report.Tasks[2].IsDueToday);
    }

    [Fact]
    public async Task The_report_lists_the_planned_meals_by_name_and_names_the_variant_that_is_planned()
    {
        await using var db = await TestDatabase.CreateAsync();
        var meals = new MealService(db.Context, db.Clock);

        var pie = await meals.CreateAsync(new CreateMealRequest("A pie"));
        var stew = await meals.CreateAsync(new CreateMealRequest("A stew"));
        await meals.CreateAsync(new CreateMealRequest("A sandwich", Status: MealStatus.Available));
        await meals.CreateAsync(new CreateMealRequest("A roast", Status: MealStatus.Queued));

        var variant = await meals.CreateVariantAsync(stew.Id, new MealVariantRequest("with dumplings"));
        await meals.PlanAsync(pie.Id, MealSlot.Dinner);
        await meals.PlanAsync(stew.Id, MealSlot.Dinner, variant!.Id, shared: true);

        var report = await Build(db);

        Assert.Equal(["A pie", "A stew (with dumplings)"], report.Meals.Select(m => m.Title));
        Assert.Equal(MealStatus.Planned, report.Meals[0].Status);
        Assert.Equal(MealStatus.PlannedShared, report.Meals[1].Status);
    }

    [Fact]
    public async Task A_source_that_is_not_configured_is_left_out_without_a_request()
    {
        await using var db = await TestDatabase.CreateAsync();
        var http = new FakeHttp().ReplyTo("calendar.google.com", EmptyCalendar);

        var report = await Build(db, http, new DailyReportOptions());

        Assert.Empty(report.Weather);
        Assert.Empty(report.Events);
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.Host is "weather.example" or "calendar.example");
    }

    [Fact]
    public async Task The_task_table_is_kept_to_the_categories_the_settings_name()
    {
        await using var db = await TestDatabase.CreateAsync();
        var categories = new CategoryService(db.Context);
        var tasks = new TaskService(db.Context, db.Clock);
        var settings = new DailyReportSettingsService(db.Context, Options.Create(new DailyReportOptions()));
        var today = Today(db.Clock);

        var house = await categories.CreateAsync(new CategoryRequest("House"));
        var garden = await categories.CreateAsync(new CategoryRequest("Garden"));

        await tasks.CreateAsync(new CreateTaskRequest("Inside", DueDate: today, CategoryId: house.Id));
        await tasks.CreateAsync(new CreateTaskRequest("Outside", DueDate: today, CategoryId: garden.Id));
        await tasks.CreateAsync(new CreateTaskRequest("Unfiled", DueDate: today));

        var everything = await Build(db, new FakeHttp(), settings);
        Assert.Equal(["Inside", "Outside", "Unfiled"], everything.Tasks.Select(t => t.Title).Order());

        await settings.SaveAsync(new UpdateDailyReportSettingsRequest(
            IsEnabled: false,
            TimeOfDay: new TimeOnly(8, 0),
            CategoryIds: [house.Id]));

        var narrowed = await Build(db, new FakeHttp(), settings);
        Assert.Equal("Inside", Assert.Single(narrowed.Tasks).Title);
    }

    [Fact]
    public async Task Each_calendar_feeds_the_section_its_kind_names()
    {
        await using var db = await TestDatabase.CreateAsync();
        var settings = new DailyReportSettingsService(db.Context, Options.Create(new DailyReportOptions()));

        foreach (var seeded in await settings.ReadCalendarsAsync())
        {
            await settings.DeleteCalendarAsync(seeded.Id);
        }

        await settings.AddCalendarAsync(new ReportCalendarInput("Mine", "https://mine.example/feed.ics"));
        await settings.AddCalendarAsync(new ReportCalendarInput("Days off", "https://days.example/feed.ics", ReportCalendarKind.Holidays));
        await settings.AddCalendarAsync(new ReportCalendarInput("The team", "https://team.example/feed.ics", ReportCalendarKind.Today));
        await settings.AddCalendarAsync(new ReportCalendarInput("Off", "https://off.example/feed.ics", ReportCalendarKind.Upcoming, IsEnabled: false));

        var http = new FakeHttp()
            .ReplyTo("mine.example", Daily("A rehearsal"))
            .ReplyTo("days.example", Daily("A day off"))
            .ReplyTo("team.example", Daily("A fixture"))
            .ReplyTo("off.example", Daily("Not read"));

        var report = await Build(db, http, settings);

        Assert.Contains(report.Events, e => e.Summary == "A rehearsal");
        Assert.DoesNotContain(report.Events, e => e.Summary == "Not read");
        Assert.Equal("A day off", report.Holidays[0].Name);

        var today = Assert.Single(report.Today);
        Assert.Equal("The team", today.Name);
        Assert.Equal("A fixture", today.Entries[0].Summary);
        Assert.DoesNotContain(http.Requests, r => r.RequestUri!.Host == "off.example");
    }

    /// <summary>A calendar holding one entry that recurs every day, so it lands in any window.</summary>
    private static string Daily(string summary) => $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:{summary}
        SUMMARY:{summary}
        DTSTART:20200101T170000Z
        DTEND:20200101T180000Z
        RRULE:FREQ=DAILY
        END:VEVENT
        END:VCALENDAR
        """;

    [Fact]
    public async Task A_source_that_fails_costs_the_report_its_own_section_and_nothing_else()
    {
        const string Forecast = """{ "period1Name": "Today", "period1Condition": "Rain", "period1Temp": 12 }""";

        await using var db = await TestDatabase.CreateAsync();
        var tasks = new TaskService(db.Context, db.Clock);
        await tasks.CreateAsync(new CreateTaskRequest("Still printed", DueDate: Today(db.Clock)));

        // The calendars, the wire and the news page all refuse; only the forecast answers.
        var http = new FakeHttp().ReplyTo("weather.example", Forecast);

        var report = await Build(db, http, new DailyReportOptions
        {
            WeatherUrl = "https://weather.example/forecast",
            CalendarUrl = "https://calendar.example/feed.ics",
        });

        Assert.Equal("Today", report.Weather[0].Name);
        Assert.Equal("Still printed", Assert.Single(report.Tasks).Title);
        Assert.Empty(report.Events);
        Assert.Empty(report.Holidays);
        Assert.Empty(report.Today);
        Assert.Empty(report.LocalNews);
        Assert.Empty(report.NationalNews);
    }

    [Fact]
    public async Task The_report_is_dated_by_the_servers_own_clock()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Clock.SetLocalTimeZone(Pacific);
        db.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 28, 5, 0, 0, TimeSpan.Zero));

        var report = await Build(db);

        // Five in the morning in London is still the evening before in Seattle.
        Assert.Equal(new DateOnly(2026, 10, 27), report.Date);
    }

    private static DateOnly Today(FakeTimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone).DateTime);

    private static Task<DailyReport> Build(TestDatabase db) =>
        Build(db, new FakeHttp(), new DailyReportOptions());

    private static Task<DailyReport> Build(TestDatabase db, FakeHttp http, DailyReportOptions options) =>
        Build(db, http, new DailyReportSettingsService(db.Context, Options.Create(options)));

    private static Task<DailyReport> Build(TestDatabase db, FakeHttp http, DailyReportSettingsService settings)
    {
        var service = new DailyReportService(
            new TaskService(db.Context, db.Clock),
            new MealService(db.Context, db.Clock),
            new WeatherFeed(http.Client()),
            new CalendarFeed(http.Client(), db.Clock),
            new NewsFeed(http.Client()),
            settings,
            db.Clock,
            NullLogger<DailyReportService>.Instance);

        return service.BuildAsync();
    }
}
