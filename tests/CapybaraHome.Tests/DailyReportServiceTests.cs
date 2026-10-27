using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
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
        Assert.Empty(report.Games);
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

    private static Task<DailyReport> Build(TestDatabase db, FakeHttp http, DailyReportOptions options)
    {
        var service = new DailyReportService(
            new TaskService(db.Context, db.Clock),
            new MealService(db.Context, db.Clock),
            new WeatherFeed(http.Client()),
            new CalendarFeed(http.Client(), db.Clock),
            new NewsFeed(http.Client()),
            Options.Create(options),
            db.Clock,
            NullLogger<DailyReportService>.Instance);

        return service.BuildAsync();
    }
}
