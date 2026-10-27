using System.Net;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Web.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Tests;

/// <summary>Covers the report as it is read on screen, downloaded and sent to the printer from there.</summary>
public class DailyReportPageTests
{
    /// <summary>An IPP answer that says the job was accepted.</summary>
    private const string Accepted = "\u0002\u0000\u0000\u0000";

    private const string Forecast = """
    { "period1Name": "Today", "period1Condition": "Rain", "period1Temp": 12, "period1WindSpeed": "15 km/h", "period1WindDirection": "SW" }
    """;

    [Fact]
    public async Task The_page_shows_this_apps_own_tasks_and_meals_and_says_what_it_could_not_read()
    {
        var feeds = new FakeHttp().ReplyTo("weather.example", Forecast);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
        {
            services.Configure<DailyReportOptions>(options => options.WeatherUrl = "https://weather.example/forecast");
            services.AddHttpClient<WeatherFeed>().ConfigurePrimaryHttpMessageHandler(feeds.Handler);
        });

        await SeedAsync(app);
        var html = await app.CreateClient().GetStringAsync("/report");

        Assert.Contains("Morning Report", html);
        Assert.Contains("Rain", html);
        Assert.Contains("15 km/h SW", html);
        Assert.Contains("An overdue job", html);
        Assert.Contains("overdue", html);
        Assert.Contains("A pie", html);
        Assert.Contains("Planned", html);
        Assert.DoesNotContain("A finished job", html);

        // The calendars and the news are refused in tests, so the page says so rather than lying.
        Assert.Contains("No upcoming events.", html);
        Assert.Contains("No holidays in the next 30 days.", html);
        Assert.Contains("No headlines available.", html);
        Assert.Contains("No printer is configured", html);
    }

    [Fact]
    public async Task The_page_shows_everything_the_calendars_and_the_news_answer_with()
    {
        const string Personal = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:1
        SUMMARY:A dentist appointment
        DTSTART:20200101T170000Z
        DTEND:20200101T180000Z
        LOCATION:The surgery
        RRULE:FREQ=DAILY
        END:VEVENT
        END:VCALENDAR
        """;

        const string Holidays = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:2
        SUMMARY:A public holiday
        DTSTART;VALUE=DATE:20200102
        DTEND;VALUE=DATE:20200103
        RRULE:FREQ=DAILY
        END:VEVENT
        END:VCALENDAR
        """;

        const string Sports = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:3
        SUMMARY:A game at home
        DTSTART:20200101T020000Z
        DTEND:20200101T050000Z
        LOCATION:The park
        RRULE:FREQ=DAILY
        END:VEVENT
        END:VCALENDAR
        """;

        const string LocalPage = """
        <html><body><a href="/article/news/1"><h3>A story from the local station</h3></a></body></html>
        """;

        const string Wire = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel>
          <item><title>A story from the wire</title><link>https://news.example/1</link></item>
        </channel></rss>
        """;

        // Every entry recurs daily from a date well in the past, so each one lands in the window
        // whenever the test is run.
        var feeds = new FakeHttp()
            .ReplyTo("calendar.example", Personal)
            .ReplyTo("en.usa", Holidays)
            .ReplyTo("mlb_", Sports)
            .ReplyTo("king5.com", LocalPage)
            .ReplyTo("feedx.net", Wire);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
        {
            services.Configure<DailyReportOptions>(options => options.CalendarUrl = "https://calendar.example/feed.ics");
            services.AddHttpClient<CalendarFeed>().ConfigurePrimaryHttpMessageHandler(feeds.Handler);
            services.AddHttpClient<NewsFeed>().ConfigurePrimaryHttpMessageHandler(feeds.Handler);
        });

        using (var scope = app.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
            await tasks.CreateAsync(new CreateTaskRequest(
                "A job due today",
                DueDate: DateOnly.FromDateTime(DateTime.UtcNow)));
        }

        var html = await app.CreateClient().GetStringAsync("/report");

        Assert.Contains("A dentist appointment", html);
        Assert.Contains("The surgery", html);
        Assert.Contains("A public holiday", html);
        Assert.Contains("Mariners Today", html);
        Assert.Contains("A game at home", html);
        Assert.Contains("A story from the local station", html);
        Assert.Contains("A story from the wire", html);
        Assert.Contains("due-today", html);

        // Nothing was planned, so the two tables this app fills say so.
        Assert.Contains("No meals planned.", html);
        Assert.Contains("Weather data unavailable.", html);
    }

    [Fact]
    public async Task The_report_can_be_downloaded_as_the_pdf_that_would_be_printed()
    {
        await using var app = await AppFactory.CreateAsync();

        var response = await app.CreateClient().GetAsync("/report/pdf");
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(body, 0, 4));
    }

    [Fact]
    public async Task The_print_button_sends_the_report_on_screen_to_the_printer()
    {
        var ipp = new FakeHttp().Reply(Accepted);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
        {
            services.Configure<DailyReportOptions>(options => options.PrinterHost = "printer.example");
            services.AddHttpClient<IppPrinter>().ConfigurePrimaryHttpMessageHandler(ipp.Handler);
        });

        await SeedAsync(app);
        var client = FormPost.Client(app);

        var printed = await FormPost.PostAsync(client, "/report", "print-report");
        var html = await printed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, printed.StatusCode);
        Assert.Contains("Sent to printer.example.", html);
        Assert.Contains("%PDF", System.Text.Encoding.ASCII.GetString(Assert.Single(ipp.Bodies)));
    }

    [Fact]
    public async Task A_printer_that_refuses_the_job_says_so_on_the_page()
    {
        var ipp = new FakeHttp().Reply(string.Empty, HttpStatusCode.ServiceUnavailable);

        await using var app = await AppFactory.CreateAsync(configureServices: services =>
        {
            services.Configure<DailyReportOptions>(options => options.PrinterHost = "printer.example");
            services.AddHttpClient<IppPrinter>().ConfigurePrimaryHttpMessageHandler(ipp.Handler);
        });

        var client = FormPost.Client(app);

        var printed = await FormPost.PostAsync(client, "/report", "print-report");
        var html = await printed.Content.ReadAsStringAsync();

        Assert.Contains("The printer answered 503", html);
    }

    private static async Task SeedAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<TaskService>();
        var meals = scope.ServiceProvider.GetRequiredService<MealService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await tasks.CreateAsync(new CreateTaskRequest("An overdue job", DueDate: today.AddDays(-1)));
        await tasks.CreateAsync(new CreateTaskRequest("A job due next month", DueDate: today.AddDays(31)));
        await tasks.CreateAsync(new CreateTaskRequest(
            "A finished job",
            Status: Domain.Tasks.TaskItemStatus.Done,
            DueDate: today));

        var pie = await meals.CreateAsync(new CreateMealRequest("A pie"));
        await meals.PlanAsync(pie.Id, MealSlot.Dinner);
        await meals.CreateAsync(new CreateMealRequest("A sandwich", Status: MealStatus.Available));
    }
}
