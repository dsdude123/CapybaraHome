namespace CapybaraHome.Domain.Reports;

/// <summary>Which section of the report a calendar feeds. Persisted by name so the database stays readable.</summary>
public enum ReportCalendarKind
{
    /// <summary>Its entries are listed as the upcoming events, with their dates and places.</summary>
    Upcoming,

    /// <summary>Its entries are listed as holidays, by name and date alone.</summary>
    Holidays,

    /// <summary>Only today's entries are shown, under a heading of the calendar's own name.</summary>
    Today,
}
