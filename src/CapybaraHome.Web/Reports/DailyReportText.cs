using System.Globalization;
using CapybaraHome.Application.Reports;

namespace CapybaraHome.Web.Reports;

/// <summary>
/// Turns the report's values into the words it is printed with. The page on screen and the printed
/// page read from here so that the two say the same thing.
/// </summary>
public static class DailyReportText
{
    /// <summary>What the report is headed with.</summary>
    public static string Heading(DateOnly date) =>
        "Morning Report — " + date.ToString("dddd, MMMM d yyyy", CultureInfo.InvariantCulture);

    /// <summary>Returns a temperature with its unit.</summary>
    public static string Temperature(string value) => $"{value} °C";

    /// <summary>Returns a due date as the report writes it, or a dash when a task has none.</summary>
    public static string DueDate(DateOnly? date) =>
        date is { } due ? due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—";

    /// <summary>Returns when a calendar entry happens, leaving the time out of an all-day one.</summary>
    public static string When(ReportEvent entry) => entry.Time is { } time
        ? $"{entry.Date.ToString("ddd MMM d", CultureInfo.InvariantCulture)} {Clock(time)}"
        : entry.Date.ToString("ddd MMM d", CultureInfo.InvariantCulture);

    /// <summary>Returns a holiday as the list writes it.</summary>
    public static string Holiday(ReportHoliday holiday) =>
        $"{holiday.Date.ToString("MMM d", CultureInfo.InvariantCulture)} — {holiday.Name}";

    /// <summary>Returns what a calendar's own section for today is headed with.</summary>
    public static string TodayHeading(ReportTodaySection section) => $"{section.Name} Today";

    /// <summary>Returns a game as the one-line section writes it, leaving out what the calendar did not say.</summary>
    public static string Game(ReportEvent game)
    {
        var parts = new List<string> { game.Summary };

        if (game.Time is { } time)
        {
            parts.Add(Clock(time));
        }

        if (game.Location.Length > 0)
        {
            parts.Add(game.Location);
        }

        return string.Join(" — ", parts);
    }

    private static string Clock(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
