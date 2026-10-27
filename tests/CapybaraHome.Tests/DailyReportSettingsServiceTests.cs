using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Reports;
using CapybaraHome.Domain.Reports;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Tests;

/// <summary>Covers the row the settings page writes, and what it refuses to be given.</summary>
public class DailyReportSettingsServiceTests
{
    [Fact]
    public async Task The_first_read_takes_the_settings_from_what_the_host_was_configured_with()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db, new DailyReportOptions
        {
            Enabled = true,
            TimeOfDay = new TimeOnly(6, 45),
            PrinterHost = "printer.lan",
            PrinterPort = 6310,
            PrinterResource = "/queue",
            WeatherUrl = "https://weather.example/forecast",
            CalendarUrl = "https://calendar.example/mine.ics",
        });

        var settings = await service.GetAsync();

        Assert.True(settings.IsEnabled);
        Assert.Equal(new TimeOnly(6, 45), settings.TimeOfDay);
        Assert.Equal("printer.lan", settings.PrinterHost);
        Assert.Equal(6310, settings.PrinterPort);
        Assert.Equal("/queue", settings.PrinterQueue);
        Assert.True(settings.PrintBothSides);
        Assert.Equal("https://weather.example/forecast", settings.WeatherUrl);
        Assert.Empty(settings.CategoryIds);
        Assert.Null(settings.LastRunAt);
        Assert.Null(settings.LastRunError);

        Assert.Equal(
            [
                (ReportCalendarKind.Upcoming, "https://calendar.example/mine.ics"),
                (ReportCalendarKind.Holidays, null),
                (ReportCalendarKind.Today, null),
            ],
            settings.Calendars.Select(c => (c.Kind, c.Url == "https://calendar.example/mine.ics" ? c.Url : null)));
        Assert.All(settings.Calendars, c => Assert.True(c.IsEnabled));
    }

    [Fact]
    public async Task A_host_that_named_no_calendar_of_its_own_still_gets_the_holidays_and_the_games()
    {
        await using var db = await TestDatabase.CreateAsync();

        var settings = await Service(db).GetAsync();

        Assert.Equal(
            [ReportCalendarKind.Holidays, ReportCalendarKind.Today],
            settings.Calendars.Select(c => c.Kind));
        Assert.Null(settings.PrinterHost);
        Assert.Equal(631, settings.PrinterPort);
        Assert.Equal("/ipp/print", settings.PrinterQueue);
    }

    [Fact]
    public async Task The_settings_are_read_back_as_they_were_saved()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var categories = new CategoryService(db.Context);

        var house = await categories.CreateAsync(new CategoryRequest("House"));

        var saved = await service.SaveAsync(new UpdateDailyReportSettingsRequest(
            IsEnabled: true,
            TimeOfDay: new TimeOnly(5, 30),
            PrinterHost: "  other.lan  ",
            PrinterPort: 9100,
            PrinterQueue: "ipp/second",
            PrintBothSides: false,
            WeatherUrl: "https://weather.example/other",
            CategoryIds: [house.Id, house.Id]));

        Assert.True(saved.IsEnabled);
        Assert.Equal(new TimeOnly(5, 30), saved.TimeOfDay);
        Assert.Equal("other.lan", saved.PrinterHost);
        Assert.Equal(9100, saved.PrinterPort);
        Assert.Equal("/ipp/second", saved.PrinterQueue);
        Assert.False(saved.PrintBothSides);
        Assert.Equal([house.Id], saved.CategoryIds);

        Assert.Equivalent(saved, await service.GetAsync());
        Assert.Equal([house.Id], await service.TrackedCategoryIdsAsync());
    }

    [Fact]
    public async Task Clearing_the_printer_and_the_forecast_leaves_the_report_with_neither()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db, new DailyReportOptions { PrinterHost = "printer.lan", WeatherUrl = "https://weather.example/f" });

        var saved = await service.SaveAsync(Request() with { PrinterHost = "   ", WeatherUrl = "" });

        Assert.Null(saved.PrinterHost);
        Assert.Null(saved.WeatherUrl);
        Assert.Equal("/ipp/print", saved.PrinterQueue);
    }

    [Fact]
    public async Task Saving_a_category_the_app_does_not_have_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(Request() with { CategoryIds = [404] }));
        Assert.Empty(await service.TrackedCategoryIdsAsync());
    }

    [Fact]
    public async Task Choosing_no_category_puts_every_category_back_on_the_report()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var house = await new CategoryService(db.Context).CreateAsync(new CategoryRequest("House"));

        await service.SaveAsync(Request() with { CategoryIds = [house.Id] });
        await service.SaveAsync(Request() with { CategoryIds = [] });

        Assert.Empty(await service.TrackedCategoryIdsAsync());
    }

    [Fact]
    public async Task A_category_the_app_loses_stops_being_tracked_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var categories = new CategoryService(db.Context);

        var house = await categories.CreateAsync(new CategoryRequest("House"));
        await service.SaveAsync(Request() with { CategoryIds = [house.Id] });

        await categories.DeleteAsync(house.Id);

        Assert.Empty(await service.TrackedCategoryIdsAsync());
    }

    [Theory]
    [InlineData("printer.lan/queue")]
    [InlineData("printer.lan:631")]
    [InlineData("two printers")]
    public async Task A_printer_is_named_by_its_host_name_alone(string host)
    {
        await using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(db).SaveAsync(Request() with { PrinterHost = host }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public async Task A_port_outside_the_range_a_port_has_is_refused(int port)
    {
        await using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(db).SaveAsync(Request() with { PrinterPort = port }));
    }

    [Theory]
    [InlineData("weather.example/forecast")]
    [InlineData("ftp://weather.example/forecast")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:secret@weather.example/forecast")]
    public async Task An_address_the_server_will_not_fetch_is_refused(string url)
    {
        await using var db = await TestDatabase.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Service(db).SaveAsync(Request() with { WeatherUrl = url }));
    }

    [Fact]
    public async Task A_forecast_on_this_network_is_allowed()
    {
        await using var db = await TestDatabase.CreateAsync();

        var saved = await Service(db).SaveAsync(Request() with { WeatherUrl = "http://192.168.1.4:8080/forecast" });

        Assert.Equal("http://192.168.1.4:8080/forecast", saved.WeatherUrl);
    }

    [Fact]
    public async Task A_calendar_is_added_at_the_end_of_the_ones_of_its_kind()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var first = await service.AddCalendarAsync(new ReportCalendarInput("Mine", "https://mine.example/feed.ics"));
        var second = await service.AddCalendarAsync(new ReportCalendarInput("Ours", "https://ours.example/feed.ics"));
        var other = await service.AddCalendarAsync(
            new ReportCalendarInput("Games", "https://games.example/feed.ics", ReportCalendarKind.Today));

        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);
        Assert.Equal(1, other.SortOrder);

        var calendars = await service.ReadCalendarsAsync();
        Assert.Equal(
            ["Mine", "Ours", "Holidays", "Mariners", "Games"],
            calendars.Select(c => c.Name));
    }

    [Theory]
    [InlineData("", "https://mine.example/feed.ics")]
    [InlineData("   ", "https://mine.example/feed.ics")]
    [InlineData("Mine", "")]
    [InlineData("Mine", "mine.example/feed.ics")]
    public async Task A_calendar_without_a_name_and_an_address_is_refused(string name, string url)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddCalendarAsync(new ReportCalendarInput(name, url)));

        var existing = await service.AddCalendarAsync(new ReportCalendarInput("Mine", "https://mine.example/feed.ics"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateCalendarAsync(existing.Id, new ReportCalendarInput(name, url)));
    }

    [Fact]
    public async Task A_calendar_keeps_its_place_when_it_is_renamed_moved_or_switched_off()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var added = await service.AddCalendarAsync(new ReportCalendarInput("Mine", "https://mine.example/feed.ics"));

        var changed = await service.UpdateCalendarAsync(
            added.Id,
            new ReportCalendarInput("Theirs", "https://theirs.example/feed.ics", ReportCalendarKind.Today, IsEnabled: false));

        Assert.NotNull(changed);
        Assert.Equal("Theirs", changed.Name);
        Assert.Equal("https://theirs.example/feed.ics", changed.Url);
        Assert.Equal(ReportCalendarKind.Today, changed.Kind);
        Assert.False(changed.IsEnabled);
        Assert.Equal(added.SortOrder, changed.SortOrder);
    }

    [Fact]
    public async Task A_calendar_that_is_not_there_is_neither_changed_nor_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        Assert.Null(await service.UpdateCalendarAsync(404, new ReportCalendarInput("Mine", "https://mine.example/feed.ics")));
        Assert.False(await service.DeleteCalendarAsync(404));
    }

    [Fact]
    public async Task A_deleted_calendar_is_gone_from_the_settings()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var added = await service.AddCalendarAsync(new ReportCalendarInput("Mine", "https://mine.example/feed.ics"));

        Assert.True(await service.DeleteCalendarAsync(added.Id));
        Assert.DoesNotContain(await service.ReadCalendarsAsync(), c => c.Id == added.Id);
    }

    [Fact]
    public async Task The_last_run_is_recorded_with_whatever_went_wrong_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var at = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);

        await service.RecordRunAsync(at, "The printer refused the job.");

        var failed = await service.GetAsync();
        Assert.Equal(at, failed.LastRunAt);
        Assert.Equal("The printer refused the job.", failed.LastRunError);

        await service.RecordRunAsync(at.AddDays(1), null);

        var printed = await service.GetAsync();
        Assert.Equal(at.AddDays(1), printed.LastRunAt);
        Assert.Null(printed.LastRunError);
    }

    [Fact]
    public async Task A_run_whose_reason_runs_on_is_cut_short_rather_than_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.RecordRunAsync(db.Clock.GetUtcNow(), new string('x', 5000));

        Assert.Equal(2000, (await service.GetAsync()).LastRunError!.Length);
    }

    [Fact]
    public async Task Saving_twice_leaves_one_row_and_one_set_of_calendars()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.SaveAsync(Request());
        await service.SaveAsync(Request() with { IsEnabled = true });

        Assert.Equal(2, (await service.GetAsync()).Calendars.Count);
        Assert.Single(db.Context.DailyReportSettings);
    }

    private static DailyReportSettingsService Service(TestDatabase db, DailyReportOptions? options = null) =>
        new(db.Context, Options.Create(options ?? new DailyReportOptions()));

    private static UpdateDailyReportSettingsRequest Request() =>
        new(IsEnabled: false, TimeOfDay: new TimeOnly(8, 0));
}
