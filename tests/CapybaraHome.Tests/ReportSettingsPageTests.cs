using System.Net;
using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Reports;
using CapybaraHome.Domain.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Tests;

/// <summary>Covers the settings page the report is turned on, timed, pointed and filtered from.</summary>
public class ReportSettingsPageTests
{
    [Fact]
    public async Task The_page_opens_on_what_the_host_was_configured_with()
    {
        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.Configure<DailyReportOptions>(options =>
            {
                options.TimeOfDay = new TimeOnly(6, 45);
                options.PrinterHost = "printer.example";
                options.WeatherUrl = "https://weather.example/forecast";
                options.CalendarUrl = "https://calendar.example/mine.ics";
            }));

        var html = await app.CreateClient().GetStringAsync("/report/settings");

        Assert.Contains("value=\"06:45\"", html);
        Assert.Contains("value=\"printer.example\"", html);
        Assert.Contains("value=\"631\"", html);
        Assert.Contains("value=\"/ipp/print\"", html);
        Assert.Contains("https://weather.example/forecast", html);
        Assert.Contains("https://calendar.example/mine.ics", html);
        Assert.Contains("My calendar", html);
        Assert.Contains("Holidays", html);
        Assert.Contains("It has not printed on a schedule yet.", html);
        Assert.Contains("Every category is in the report.", html);
        Assert.Contains("There are no categories yet.", html);

        // Every kind is offered, on the row of each calendar and on the add form.
        foreach (var kind in CapybaraHome.Web.ReportCalendarKindNames.All)
        {
            Assert.Contains($"value=\"{kind}\"", html);
            Assert.Contains(CapybaraHome.Web.ReportCalendarKindNames.Label(kind), html);
        }
    }

    [Fact]
    public async Task Saving_the_form_writes_the_schedule_the_printer_the_forecast_and_the_categories()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);
        var category = await CategoryAsync(app, "House");

        var response = await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["enabled"] = "true",
            ["time-of-day"] = "05:30",
            ["printer-host"] = "other.example",
            ["printer-port"] = "9100",
            ["printer-queue"] = "ipp/second",
            ["both-sides"] = "true",
            ["weather-url"] = "http://weather.lan/forecast",
            ["category"] = category.Id.ToString(),
        });

        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Saved.", html);
        Assert.Contains("value=\"05:30\"", html);
        Assert.Contains("value=\"other.example\"", html);
        Assert.Contains("value=\"9100\"", html);
        Assert.Contains("value=\"/ipp/second\"", html);
        Assert.Contains("Only these categories are in the report.", html);

        var settings = await SettingsAsync(app);
        Assert.True(settings.IsEnabled);
        Assert.Equal(new TimeOnly(5, 30), settings.TimeOfDay);
        Assert.Equal("other.example", settings.PrinterHost);
        Assert.Equal(9100, settings.PrinterPort);
        Assert.Equal("/ipp/second", settings.PrinterQueue);
        Assert.True(settings.PrintBothSides);
        Assert.Equal("http://weather.lan/forecast", settings.WeatherUrl);
        Assert.Equal([category.Id], settings.CategoryIds);
    }

    [Fact]
    public async Task A_box_left_unticked_turns_its_setting_off()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);
        var category = await CategoryAsync(app, "House");

        await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["enabled"] = "true",
            ["time-of-day"] = "05:30",
            ["both-sides"] = "true",
            ["category"] = category.Id.ToString(),
        });

        // A browser sends nothing at all for a box the reader unticked.
        var response = await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["time-of-day"] = "05:30",
            ["printer-queue"] = "/ipp/print",
            ["printer-port"] = "631",
        });

        Assert.Contains("Every category is in the report.", await response.Content.ReadAsStringAsync());

        var settings = await SettingsAsync(app);
        Assert.False(settings.IsEnabled);
        Assert.False(settings.PrintBothSides);
        Assert.Empty(settings.CategoryIds);
        Assert.Null(settings.PrinterHost);
        Assert.Null(settings.WeatherUrl);
    }

    [Fact]
    public async Task A_printer_or_an_address_the_report_cannot_use_is_refused_on_the_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var refused = await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["time-of-day"] = "08:00",
            ["printer-host"] = "printer.example:631",
            ["printer-port"] = "631",
        });

        Assert.Contains("A printer is named by its host name alone", await refused.Content.ReadAsStringAsync());
        Assert.Null((await SettingsAsync(app)).PrinterHost);

        var badPort = await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["time-of-day"] = "08:00",
            ["printer-port"] = "0",
        });

        Assert.Contains("A port is between 1 and 65535", await badPort.Content.ReadAsStringAsync());

        var badAddress = await FormPost.PostAsync(client, "/report/settings", "report-settings", new()
        {
            ["time-of-day"] = "08:00",
            ["printer-port"] = "631",
            ["weather-url"] = "weather.lan/forecast",
        });

        Assert.Contains("An address starts with http:// or https://", await badAddress.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(ReportCalendarKind.Upcoming)]
    [InlineData(ReportCalendarKind.Holidays)]
    [InlineData(ReportCalendarKind.Today)]
    public async Task A_calendar_of_any_kind_can_be_added_from_the_page(ReportCalendarKind kind)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var response = await FormPost.PostAsync(client, "/report/settings", "add-calendar", new()
        {
            ["name"] = "A new feed",
            ["url"] = "https://new.example/basic.ics",
            ["kind"] = kind.ToString(),
        });

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("A new feed", html);
        Assert.Contains("https://new.example/basic.ics", html);
        Assert.Contains(
            System.Text.Encodings.Web.HtmlEncoder.Default.Encode(CapybaraHome.Web.ReportCalendarKindNames.Description(kind)),
            html);

        var added = Assert.Single((await SettingsAsync(app)).Calendars, c => c.Name == "A new feed");
        Assert.Equal(kind, added.Kind);
        Assert.True(added.IsEnabled);
    }

    [Fact]
    public async Task A_calendar_without_a_name_keeps_what_was_typed_and_says_what_is_missing()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var response = await FormPost.PostAsync(client, "/report/settings", "add-calendar", new()
        {
            ["name"] = "   ",
            ["url"] = "https://new.example/basic.ics",
            ["kind"] = nameof(ReportCalendarKind.Upcoming),
        });

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("A calendar needs a name.", html);
        Assert.Contains("value=\"https://new.example/basic.ics\"", html);
        Assert.DoesNotContain((await SettingsAsync(app)).Calendars, c => c.Url == "https://new.example/basic.ics");
    }

    [Fact]
    public async Task A_calendar_row_can_be_renamed_repointed_moved_and_switched_off()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var holidays = Assert.Single((await SettingsAsync(app)).Calendars, c => c.Kind == ReportCalendarKind.Holidays);

        var response = await FormPost.PostAsync(client, "/report/settings", $"calendar-{holidays.Id}", new()
        {
            ["name"] = "Days off",
            ["url"] = "https://days.example/basic.ics",
            ["kind"] = nameof(ReportCalendarKind.Today),
        });

        Assert.Contains("Days off", await response.Content.ReadAsStringAsync());

        var changed = Assert.Single((await SettingsAsync(app)).Calendars, c => c.Id == holidays.Id);
        Assert.Equal("Days off", changed.Name);
        Assert.Equal("https://days.example/basic.ics", changed.Url);
        Assert.Equal(ReportCalendarKind.Today, changed.Kind);
        Assert.False(changed.IsEnabled);
    }

    [Fact]
    public async Task A_calendar_row_that_is_given_no_address_says_so()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var holidays = Assert.Single((await SettingsAsync(app)).Calendars, c => c.Kind == ReportCalendarKind.Holidays);

        var response = await FormPost.PostAsync(client, "/report/settings", $"calendar-{holidays.Id}", new()
        {
            ["name"] = "Days off",
            ["url"] = string.Empty,
            ["kind"] = nameof(ReportCalendarKind.Holidays),
            ["enabled"] = "true",
        });

        Assert.Contains("A calendar needs an address.", await response.Content.ReadAsStringAsync());
        Assert.Equal(holidays.Url, Assert.Single((await SettingsAsync(app)).Calendars, c => c.Id == holidays.Id).Url);
    }

    [Fact]
    public async Task A_calendar_can_be_deleted_from_the_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = FormPost.Client(app);

        var calendars = (await SettingsAsync(app)).Calendars;

        foreach (var calendar in calendars)
        {
            await FormPost.PostAsync(client, "/report/settings", $"delete-calendar-{calendar.Id}");
        }

        var html = await client.GetStringAsync("/report/settings");

        Assert.Contains("No calendars yet", html);
        Assert.Empty((await SettingsAsync(app)).Calendars);
    }

    [Fact]
    public async Task The_page_says_how_the_last_scheduled_printing_went()
    {
        await using var app = await AppFactory.CreateAsync();

        await RecordAsync(app, new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero), "The printer answered 503.");
        Assert.Contains("did not print: The printer answered 503.", await app.CreateClient().GetStringAsync("/report/settings"));

        await RecordAsync(app, new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero), null);
        Assert.Contains("Last printed", await app.CreateClient().GetStringAsync("/report/settings"));
    }

    [Fact]
    public async Task The_report_links_to_its_settings()
    {
        await using var app = await AppFactory.CreateAsync();

        Assert.Contains("report/settings", await app.CreateClient().GetStringAsync("/report"));
    }

    private static async Task<CategoryView> CategoryAsync(AppFactory app, string name)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CategoryService>().CreateAsync(new CategoryRequest(name));
    }

    private static async Task<DailyReportSettingsView> SettingsAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DailyReportSettingsService>().GetAsync();
    }

    private static async Task RecordAsync(AppFactory app, DateTimeOffset at, string? error)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DailyReportSettingsService>().RecordRunAsync(at, error);
    }
}
