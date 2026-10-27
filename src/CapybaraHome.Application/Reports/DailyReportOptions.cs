namespace CapybaraHome.Application.Reports;

/// <summary>Settings for the daily report, bound from the <c>DailyReport</c> configuration section.</summary>
public class DailyReportOptions
{
    /// <summary>Configuration section these settings are bound from.</summary>
    public const string SectionName = "DailyReport";

    /// <summary>Whether the report is built and printed on a schedule. The page is there either way.</summary>
    public bool Enabled { get; set; }

    /// <summary>Time of day the report is printed, read in the time zone the server runs in.</summary>
    public TimeOnly TimeOfDay { get; set; } = new(8, 0);

    /// <summary>Host name of the IPP printer. With none set the scheduled report is built but not printed.</summary>
    public string? PrinterHost { get; set; }

    /// <summary>Port the printer answers IPP on.</summary>
    public int PrinterPort { get; set; } = 631;

    /// <summary>Path the printer's IPP queue lives at.</summary>
    public string PrinterResource { get; set; } = "/ipp/print";

    /// <summary>
    /// Address of the forecast service, which answers with the fields <c>period1Name</c> through
    /// <c>period4WindDirection</c>. With none set the report leaves the weather out.
    /// </summary>
    public string? WeatherUrl { get; set; }

    /// <summary>Address of the personal calendar, as an iCalendar feed. With none set the report leaves events out.</summary>
    public string? CalendarUrl { get; set; }
}
