using CapybaraHome.Domain.Reports;

namespace CapybaraHome.Web;

/// <summary>Turns the calendar kinds into the words the settings page shows.</summary>
public static class ReportCalendarKindNames
{
    /// <summary>Every kind, in the order the report puts their sections in.</summary>
    public static readonly ReportCalendarKind[] All =
    [
        ReportCalendarKind.Upcoming,
        ReportCalendarKind.Holidays,
        ReportCalendarKind.Today,
    ];

    /// <summary>Returns the label for <paramref name="kind"/>.</summary>
    public static string Label(ReportCalendarKind kind) => kind switch
    {
        ReportCalendarKind.Upcoming => "Upcoming events",
        ReportCalendarKind.Holidays => "Holidays",
        ReportCalendarKind.Today => "Today only",
        _ => kind.ToString(),
    };

    /// <summary>Returns what the report does with a calendar of <paramref name="kind"/>.</summary>
    public static string Description(ReportCalendarKind kind) => kind switch
    {
        ReportCalendarKind.Upcoming => "Listed with dates and places for the next 30 days.",
        ReportCalendarKind.Holidays => "Listed by name and date for the next 30 days.",
        ReportCalendarKind.Today => "Only today's entries, under a heading of the calendar's name.",
        _ => string.Empty,
    };
}
