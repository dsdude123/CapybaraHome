using System.Net;
using System.Text;
using CapybaraHome.Application.Abstractions;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Reports;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>Covers the printed page itself: how it is laid out, how it reaches the printer and when.</summary>
public class DailyReportPrintTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    /// <summary>An IPP answer that says the job was accepted: version, then status 0x0000.</summary>
    private const string Accepted = "\u0002\u0000\u0000\u0000";

    [Fact]
    public void A_full_report_is_laid_out_as_a_pdf()
    {
        var pdf = DailyReportPdf.Render(Full());

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 1000, $"The report came to {pdf.Length} bytes, which is too small to be a page.");
    }

    [Fact]
    public void A_report_whose_sources_all_failed_is_still_laid_out()
    {
        var pdf = DailyReportPdf.Render(new DailyReport(new DateOnly(2026, 10, 27), [], [], [], [], [], [], [], []));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task The_print_job_carries_the_document_and_the_attributes_the_printer_needs()
    {
        var http = new FakeHttp().Reply(Accepted);
        var printer = new IppPrinter(http.Client());
        var document = Encoding.ASCII.GetBytes("%PDF-1.4 pretend");

        await printer.PrintAsync("printer.example", 631, "ipp/print", document, "Morning Report");

        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://printer.example:631/ipp/print", request.RequestUri!.ToString());
        Assert.Equal("application/ipp", request.Content!.Headers.ContentType!.MediaType);

        var body = Assert.Single(http.Bodies);
        var text = Encoding.ASCII.GetString(body);

        // Version 2.0, then Print-Job, then request id 1.
        Assert.Equal<byte[]>([0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01], body[..8]);
        Assert.Contains("printer-uri", text);
        Assert.Contains("ipp://printer.example:631/ipp/print", text);
        Assert.Contains("job-name", text);
        Assert.Contains("Morning Report", text);
        Assert.Contains("application/pdf", text);
        Assert.Contains("two-sided-long-edge", text);
        Assert.EndsWith("%PDF-1.4 pretend", text);
    }

    [Fact]
    public async Task A_printer_that_cannot_be_reached_or_refuses_the_job_is_reported()
    {
        var refused = new IppPrinter(new FakeHttp().Reply(string.Empty, HttpStatusCode.NotFound).Client());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            refused.PrintAsync("printer.example", 631, "/ipp/print", [1], "Morning Report"));

        // 0x0501 is server-error-internal-error, which the printer answers with over a 200.
        var broken = new IppPrinter(new FakeHttp().Reply("\u0002\u0000\u0005\u0001").Client());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broken.PrintAsync("printer.example", 631, "/ipp/print", [1], "Morning Report"));

        var mute = new IppPrinter(new FakeHttp().Reply(string.Empty).Client());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mute.PrintAsync("printer.example", 631, "/ipp/print", [1], "Morning Report"));

        var nowhere = new IppPrinter(new FakeHttp().Reply(Accepted).Client());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nowhere.PrintAsync(null, 631, "/ipp/print", [1], "Morning Report"));
    }

    [Theory]
    // An ordinary morning.
    [InlineData("2026-10-27", 8, 0, "2026-10-27T15:00:00Z")]
    // Winter, when the same wall clock time is an hour later in UTC.
    [InlineData("2026-12-01", 8, 0, "2026-12-01T16:00:00Z")]
    // A time of day the clocks going forward skip, which moves to the first minute that exists.
    [InlineData("2027-03-14", 2, 30, "2027-03-14T10:00:00Z")]
    public void The_printing_time_is_read_in_the_servers_own_zone(string day, int hour, int minute, string expected)
    {
        var instant = DailyReportRunner.Instant(
            DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture),
            new TimeOnly(hour, minute),
            Pacific);

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), instant);
    }

    [Fact]
    public async Task With_the_schedule_off_nothing_is_printed()
    {
        await using var db = await TestDatabase.CreateAsync();
        At(db, "2026-10-27T15:01:00Z");

        var ipp = new FakeHttp().Reply(Accepted);
        await Settings(db).SaveAsync(new UpdateDailyReportSettingsRequest(
            IsEnabled: false,
            TimeOfDay: new TimeOnly(8, 0),
            PrinterHost: "printer.example"));

        await using var services = Host(db, ipp);

        await SweepAsync(services, db);

        Assert.Empty(ipp.Requests);
    }

    [Fact]
    public async Task The_report_is_printed_once_the_time_of_day_comes_round_and_not_again_that_day()
    {
        await using var db = await TestDatabase.CreateAsync();
        At(db, "2026-10-27T14:59:00Z");

        var tasks = new TaskService(db.Context, db.Clock);
        await tasks.CreateAsync(new CreateTaskRequest("Printed at eight", DueDate: new DateOnly(2026, 10, 27)));

        var ipp = new FakeHttp().Reply(Accepted);
        await Scheduled(db);
        await using var services = Host(db, ipp);

        // A minute before, nothing.
        await SweepAsync(services, db);
        Assert.Empty(ipp.Requests);

        db.Clock.Advance(TimeSpan.FromMinutes(2));
        await SweepAsync(services, db);

        Assert.Single(ipp.Requests);
        Assert.Contains("%PDF", Encoding.ASCII.GetString(ipp.Bodies[0]));

        // The sweep runs every minute; the report is not printed again on the next one.
        db.Clock.Advance(TimeSpan.FromMinutes(1));
        await SweepAsync(services, db);
        Assert.Single(ipp.Requests);

        var settings = services.GetRequiredService<DailyReportSettingsService>();
        var saved = await settings.GetAsync();
        Assert.NotNull(saved.LastRunAt);
        Assert.Null(saved.LastRunError);
    }

    [Fact]
    public async Task A_morning_the_app_was_down_for_is_missed_rather_than_printed_late()
    {
        await using var db = await TestDatabase.CreateAsync();
        At(db, "2026-10-27T17:00:00Z");

        var ipp = new FakeHttp().Reply(Accepted);
        await Scheduled(db);
        await using var services = Host(db, ipp);

        await SweepAsync(services, db);

        Assert.Empty(ipp.Requests);
    }

    [Fact]
    public async Task A_printer_that_refuses_the_job_leaves_the_reason_on_the_settings()
    {
        await using var db = await TestDatabase.CreateAsync();
        At(db, "2026-10-27T15:01:00Z");

        var ipp = new FakeHttp().Reply(string.Empty, HttpStatusCode.ServiceUnavailable);
        await Scheduled(db);
        await using var services = Host(db, ipp);

        await SweepAsync(services, db);

        var saved = await services.GetRequiredService<DailyReportSettingsService>().GetAsync();
        Assert.NotNull(saved.LastRunAt);
        Assert.Contains("503", saved.LastRunError);
    }

    [Fact]
    public async Task The_hosted_runner_sweeps_as_soon_as_it_starts()
    {
        await using var db = await TestDatabase.CreateAsync();
        At(db, "2026-10-27T15:01:00Z");

        var ipp = new FakeHttp().Reply(Accepted);
        await Scheduled(db);
        await using var services = Host(db, ipp);
        var runner = Runner(services, db);

        await runner.StartAsync(CancellationToken.None);

        for (var step = 0; step < 200 && ipp.Requests.Count == 0; step++)
        {
            await Task.Delay(10);
        }

        await runner.StopAsync(CancellationToken.None);

        Assert.Single(ipp.Requests);
        Assert.True(services.GetRequiredService<SweepWatch>().Done);
    }

    /// <summary>Runs one sweep against the services a test built.</summary>
    private static Task SweepAsync(ServiceProvider services, TestDatabase db) =>
        Runner(services, db).SweepAsync();

    private static DailyReportRunner Runner(ServiceProvider services, TestDatabase db) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<ILogger<DailyReportRunner>>(),
        db.Clock);

    /// <summary>Turns the schedule on for eight in the morning, with a printer to send it to.</summary>
    private static Task Scheduled(TestDatabase db) =>
        Settings(db).SaveAsync(new UpdateDailyReportSettingsRequest(
            IsEnabled: true,
            TimeOfDay: new TimeOnly(8, 0),
            PrinterHost: "printer.example"));

    private static DailyReportSettingsService Settings(TestDatabase db) => new(db.Context);

    private static void At(TestDatabase db, string instant)
    {
        db.Clock.SetLocalTimeZone(Pacific);
        db.Clock.SetUtcNow(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A report with something in every section, so the layout is exercised whole.</summary>
    private static DailyReport Full() => new(
        new DateOnly(2026, 10, 27),
        [new WeatherPeriod("Today", "Rain", "12", "15 km/h SW")],
        [
            new ReportTask(1, "An overdue job", new DateOnly(2026, 10, 20), true, false),
            new ReportTask(2, "A job for today", new DateOnly(2026, 10, 27), false, true),
            new ReportTask(3, "A job for later", new DateOnly(2026, 11, 1), false, false),
            new ReportTask(4, "A job with no date", null, false, false),
        ],
        [new ReportMeal(5, "A pie", MealStatus.Planned), new ReportMeal(6, "A stew", MealStatus.PlannedShared)],
        [
            new ReportEvent("A timed event", new DateOnly(2026, 10, 28), new TimeOnly(19, 0), "The hall"),
            new ReportEvent("A whole day", new DateOnly(2026, 10, 30), null, string.Empty),
        ],
        [new ReportHoliday("A holiday", new DateOnly(2026, 11, 11))],
        [new ReportTodaySection("Mariners Game", [new ReportEvent("A game", new DateOnly(2026, 10, 27), new TimeOnly(13, 0), "The park")])],
        [new NewsHeadline("A local story", "https://news.example/1")],
        [new NewsHeadline("A national story", "https://news.example/2")]);

    /// <summary>Set once the runner has opened a scope, which is how a test knows a sweep has run.</summary>
    private sealed class SweepWatch
    {
        public bool Done { get; set; }
    }

    /// <summary>Builds just enough of the app for the runner to resolve what it prints.</summary>
    private static ServiceProvider Host(TestDatabase db, FakeHttp ipp)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<TimeProvider>(db.Clock);

        // Registered as an instance the test owns: a scope the runner opens and closes must not
        // take the shared context down with it.
        services.AddSingleton<ICapybaraHomeDbContext>(db.Context);
        services.AddScoped<TaskService>();
        services.AddScoped<MealService>();
        services.AddScoped(_ => new WeatherFeed(new FakeHttp().Client()));
        services.AddScoped(_ => new CalendarFeed(new FakeHttp().Client(), db.Clock));
        services.AddScoped(_ => new NewsFeed(new FakeHttp().Client()));
        services.AddScoped<DailyReportService>();
        services.AddScoped(_ => new IppPrinter(ipp.Client()));
        services.AddScoped(provider =>
        {
            provider.GetRequiredService<SweepWatch>().Done = true;
            return new DailyReportSettingsService(db.Context);
        });
        services.AddScoped<DailyReportPrinting>();
        services.AddSingleton<SweepWatch>();

        return services.BuildServiceProvider();
    }
}
