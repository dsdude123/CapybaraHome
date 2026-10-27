namespace CapybaraHome.Domain.Reports;

/// <summary>An iCalendar feed the report reads, and the section it feeds.</summary>
public class ReportCalendar
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>What the calendar is called, which is what a Today section is headed with.</summary>
    public required string Name { get; set; }

    /// <summary>Address of the iCalendar feed.</summary>
    public required string Url { get; set; }

    /// <summary>Which section of the report it feeds.</summary>
    public ReportCalendarKind Kind { get; set; } = ReportCalendarKind.Upcoming;

    /// <summary>Whether the report reads it at all.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Where it sits among the calendars of its kind, counted from zero.</summary>
    public int SortOrder { get; set; }
}
