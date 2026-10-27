using System.Net;
using CapybaraHome.Application.Reports;
using Microsoft.Extensions.Time.Testing;

namespace CapybaraHome.Tests;

/// <summary>Covers the three sources the report reads from outside the app.</summary>
public class DailyReportFeedTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    [Fact]
    public async Task The_forecast_is_read_as_four_periods_whether_its_numbers_are_quoted_or_not()
    {
        const string Forecast = """
        {
          "period1Name": "Today", "period1Condition": "Rain", "period1Temp": 12, "period1WindSpeed": "15 km/h", "period1WindDirection": "SW",
          "period2Name": "Tonight", "period2Condition": "Cloudy", "period2Temp": "8", "period2WindSpeed": "10 km/h", "period2WindDirection": "S",
          "period3Name": "Tomorrow", "period3Condition": "Sun", "period3Temp": 15.5
        }
        """;

        var http = new FakeHttp().Reply(Forecast);
        var feed = new WeatherFeed(http.Client());

        var periods = await feed.ReadAsync("https://weather.example/forecast");

        Assert.Equal(4, periods.Count);
        Assert.Equal("Today", periods[0].Name);
        Assert.Equal("12", periods[0].Temperature);
        Assert.Equal("15 km/h SW", periods[0].Wind);
        Assert.Equal("8", periods[1].Temperature);

        // A period the service did not answer with is still a row, as it was before the port.
        Assert.Equal("15.5", periods[2].Temperature);
        Assert.Equal(string.Empty, periods[2].Wind);
        Assert.Equal(string.Empty, periods[3].Name);
    }

    [Fact]
    public async Task A_forecast_field_that_is_neither_a_word_nor_a_number_is_read_as_best_it_can_be()
    {
        const string Forecast = """
        { "period1Name": true, "period1Condition": null, "period1Temp": [], "period1WindSpeed": "calm" }
        """;

        var feed = new WeatherFeed(new FakeHttp().Reply(Forecast).Client());

        var periods = await feed.ReadAsync("https://weather.example/forecast");

        Assert.Equal("True", periods[0].Name);
        Assert.Equal(string.Empty, periods[0].Condition);
        Assert.Equal(string.Empty, periods[0].Temperature);
        Assert.Equal("calm", periods[0].Wind);
    }

    [Fact]
    public async Task A_forecast_that_cannot_be_read_is_reported_to_its_caller()
    {
        var http = new FakeHttp().Reply(string.Empty, HttpStatusCode.ServiceUnavailable);
        var feed = new WeatherFeed(http.Client());

        await Assert.ThrowsAsync<HttpRequestException>(() => feed.ReadAsync("https://weather.example/forecast"));
    }

    [Fact]
    public async Task A_calendar_is_expanded_over_the_window_and_read_in_the_servers_own_zone()
    {
        const string Calendar = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:1
        SUMMARY:Every Monday
        DTSTART;TZID=America/Los_Angeles:20261026T190000
        DTEND;TZID=America/Los_Angeles:20261026T210000
        LOCATION:The hall
        RRULE:FREQ=WEEKLY;COUNT=4
        END:VEVENT
        BEGIN:VEVENT
        UID:2
        SUMMARY:A whole day
        DTSTART;VALUE=DATE:20261030
        DTEND;VALUE=DATE:20261031
        END:VEVENT
        BEGIN:VEVENT
        UID:3
        SUMMARY:Long past
        DTSTART;TZID=America/Los_Angeles:20260101T090000
        DTEND;TZID=America/Los_Angeles:20260101T100000
        END:VEVENT
        BEGIN:VEVENT
        UID:4
        SUMMARY:Far ahead
        DTSTART;VALUE=DATE:20270301
        DTEND;VALUE=DATE:20270302
        END:VEVENT
        END:VCALENDAR
        """;

        var feed = new CalendarFeed(new FakeHttp().Reply(Calendar).Client(), Clock());

        var entries = await feed.ReadAsync(
            "https://calendar.example/feed.ics",
            new DateOnly(2026, 10, 27),
            new DateOnly(2026, 11, 26));

        Assert.Equal(["A whole day", "Every Monday", "Every Monday", "Every Monday"], entries.Select(e => e.Summary));

        var allDay = entries[0];
        Assert.Equal(new DateOnly(2026, 10, 30), allDay.Date);
        Assert.Null(allDay.Time);

        var weekly = entries[1];
        Assert.Equal(new DateOnly(2026, 11, 2), weekly.Date);
        Assert.Equal(new TimeOnly(19, 0), weekly.Time);
        Assert.Equal("The hall", weekly.Location);
    }

    [Fact]
    public async Task A_stamped_start_is_turned_into_the_local_clock_time()
    {
        const string Calendar = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//tests//EN
        BEGIN:VEVENT
        UID:1
        SUMMARY:A game
        DTSTART:20261027T200000Z
        DTEND:20261027T230000Z
        LOCATION:The park
        END:VEVENT
        BEGIN:VEVENT
        UID:2
        SUMMARY:Tomorrow's game
        DTSTART:20261028T200000Z
        DTEND:20261028T230000Z
        END:VEVENT
        END:VCALENDAR
        """;

        var feed = new CalendarFeed(new FakeHttp().Reply(Calendar).Client(), Clock());

        var today = await feed.ReadDayAsync("https://calendar.example/sports.ics", new DateOnly(2026, 10, 27));

        var game = Assert.Single(today);
        Assert.Equal("A game", game.Summary);
        Assert.Equal(new TimeOnly(13, 0), game.Time);
        Assert.Equal("The park", game.Location);
    }

    [Fact]
    public async Task A_feed_is_read_whether_it_is_written_as_rss_or_as_atom()
    {
        const string Rss = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel>
          <title>A wire</title>
          <item><title>First story</title><link>https://news.example/1</link></item>
          <item><title>Second story</title><link>https://news.example/2</link></item>
          <item><title>Third story</title><link>https://news.example/3</link></item>
          <item><title>Why PB&amp;amp;J is still king</title><link>https://news.example/4</link></item>
        </channel></rss>
        """;

        const string Atom = """
        <?xml version="1.0"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>A wire</title>
          <entry><title>An entry</title><link rel="alternate" href="https://news.example/a" /></entry>
        </feed>
        """;

        var feed = new NewsFeed(new FakeHttp().ReplyTo("rss", Rss).ReplyTo("atom", Atom).Client());

        var rss = await feed.ReadFeedAsync("https://news.example/rss.xml", 4);
        Assert.Equal(
            ["First story", "Second story", "Third story", "Why PB&J is still king"],
            rss.Select(h => h.Title));
        Assert.Equal("https://news.example/1", rss[0].Link);

        var atom = await feed.ReadFeedAsync("https://news.example/atom.xml", 5);
        var entry = Assert.Single(atom);
        Assert.Equal("An entry", entry.Title);
        Assert.Equal("https://news.example/a", entry.Link);
    }

    [Fact]
    public async Task The_news_page_gives_up_its_stories_and_leaves_everything_else_behind()
    {
        const string Page = """
        <html><body>
          <a href="/local/traffic">A section, not a story</a>
          <a href="/article/news/first-story/123"><h3>  A real story about the county  </h3></a>
          <a href="/article/news/first-story/123"><h3>A real story about the county</h3></a>
          <a href="/article/news/short/124"><h3>Too short</h3></a>
          <a href="/article/weather/125"><h3>Seattle weather forecast for the weekend</h3></a>
          <a href="/article/news/explainer/126"><h3>How to file your property taxes this year</h3></a>
          <a href="/article/news/second-story/127">A second real story, from the link text</a>
          <a href="/article/news/third-story/128"><h2>A third real story worth printing</h2></a>
          <a href="/article/news/fourth-story/129"><h2>A fourth real story worth printing</h2></a>
        </body></html>
        """;

        var feed = new NewsFeed(new FakeHttp().Reply(Page).Client());

        var headlines = await feed.ReadPageAsync("https://news.example/local", 3);

        Assert.Equal(
            [
                "A real story about the county",
                "A second real story, from the link text",
                "A third real story worth printing",
            ],
            headlines.Select(h => h.Title));
        Assert.Equal("/article/news/first-story/123", headlines[0].Link);
    }

    [Fact]
    public async Task The_news_page_is_asked_for_as_a_browser_would_ask()
    {
        var http = new FakeHttp().Reply("<html><body></body></html>");
        var feed = new NewsFeed(http.Client());

        var headlines = await feed.ReadPageAsync("https://news.example/local", 5);

        Assert.Empty(headlines);
        var agent = string.Join(" ", Assert.Single(http.Requests).Headers.GetValues("User-Agent"));
        Assert.Contains("Chrome", agent);
    }

    private static FakeTimeProvider Clock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
        clock.SetLocalTimeZone(Pacific);
        return clock;
    }
}
