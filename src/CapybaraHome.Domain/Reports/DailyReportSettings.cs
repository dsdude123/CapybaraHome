namespace CapybaraHome.Domain.Reports;

/// <summary>
/// The one row that says how the morning report is built and printed. It is created on first
/// run from whatever the host was configured with, and the settings page owns it after that.
/// </summary>
public class DailyReportSettings
{
    /// <summary>The id the single row always has.</summary>
    public const int SingletonId = 1;

    /// <summary>Primary key, always <see cref="SingletonId"/>.</summary>
    public int Id { get; set; } = SingletonId;

    /// <summary>Whether the report is printed on a schedule. The page is there either way.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Time of day the report is printed, read in the time zone the server runs in.</summary>
    public TimeOnly TimeOfDay { get; set; } = new(8, 0);

    /// <summary>Host name of the IPP printer, or null when there is nowhere to print.</summary>
    public string? PrinterHost { get; set; }

    /// <summary>Port the printer answers IPP on.</summary>
    public int PrinterPort { get; set; } = 631;

    /// <summary>Path the printer's IPP queue lives at.</summary>
    public string PrinterQueue { get; set; } = "/ipp/print";

    /// <summary>Whether the job asks for both sides of the paper.</summary>
    public bool PrintBothSides { get; set; } = true;

    /// <summary>Address of the forecast service, or null to leave the weather out.</summary>
    public string? WeatherUrl { get; set; }

    /// <summary>When the report was last printed by the schedule, or null while it never has.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>What went wrong with that run, or null when it printed.</summary>
    public string? LastRunError { get; set; }
}
