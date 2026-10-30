using CapybaraHome.Domain.Reports;

namespace CapybaraHome.Application.Reports;

/// <summary>A calendar feed as the settings page shows it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Name">What the calendar is called.</param>
/// <param name="Url">Address of the iCalendar feed.</param>
/// <param name="Kind">Which section of the report it feeds.</param>
/// <param name="IsEnabled">Whether the report reads it.</param>
/// <param name="SortOrder">Where it sits among the calendars of its kind.</param>
public record ReportCalendarView(
    int Id,
    string Name,
    string Url,
    ReportCalendarKind Kind,
    bool IsEnabled,
    int SortOrder);

/// <summary>Fields accepted when adding or changing a calendar.</summary>
/// <param name="Name">What the calendar is called; required.</param>
/// <param name="Url">Address of the iCalendar feed; required.</param>
/// <param name="Kind">Which section of the report it feeds.</param>
/// <param name="IsEnabled">Whether the report reads it.</param>
public record ReportCalendarInput(
    string Name,
    string Url,
    ReportCalendarKind Kind = ReportCalendarKind.Upcoming,
    bool IsEnabled = true);

/// <summary>How the report is built and printed, as the page and the API show it.</summary>
/// <param name="IsEnabled">Whether it is printed on a schedule.</param>
/// <param name="TimeOfDay">Time of day it is printed, in the server's time zone.</param>
/// <param name="PrinterHost">Host name of the IPP printer, or null when there is nowhere to print.</param>
/// <param name="PrinterPort">Port the printer answers IPP on.</param>
/// <param name="PrinterQueue">Path the printer's IPP queue lives at.</param>
/// <param name="PrintBothSides">Whether the job asks for both sides of the paper.</param>
/// <param name="WeatherUrl">Address of the forecast service, or null to leave the weather out.</param>
/// <param name="CategoryIds">Categories the task table is kept to; empty carries every category.</param>
/// <param name="Calendars">The calendar feeds, by kind and then by their order.</param>
/// <param name="LastRunAt">When the schedule last printed the report, or null while it never has.</param>
/// <param name="LastRunError">What went wrong with that run, or null when it printed.</param>
public record DailyReportSettingsView(
    bool IsEnabled,
    TimeOnly TimeOfDay,
    string? PrinterHost,
    int PrinterPort,
    string PrinterQueue,
    bool PrintBothSides,
    string? WeatherUrl,
    IReadOnlyList<int> CategoryIds,
    IReadOnlyList<ReportCalendarView> Calendars,
    DateTimeOffset? LastRunAt,
    string? LastRunError);

/// <summary>Fields accepted when saving the report's settings. Every one of them is written.</summary>
/// <param name="IsEnabled">Whether it is printed on a schedule.</param>
/// <param name="TimeOfDay">Time of day it is printed, in the server's time zone.</param>
/// <param name="PrinterHost">Host name of the IPP printer; blank leaves nowhere to print.</param>
/// <param name="PrinterPort">Port the printer answers IPP on.</param>
/// <param name="PrinterQueue">Path the printer's IPP queue lives at; blank means <c>/ipp/print</c>.</param>
/// <param name="PrintBothSides">Whether the job asks for both sides of the paper.</param>
/// <param name="WeatherUrl">Address of the forecast service; blank leaves the weather out.</param>
/// <param name="CategoryIds">Categories the task table is kept to; empty carries every category.</param>
public record UpdateDailyReportSettingsRequest(
    bool IsEnabled,
    TimeOnly TimeOfDay,
    string? PrinterHost = null,
    int PrinterPort = 631,
    string? PrinterQueue = null,
    bool PrintBothSides = true,
    string? WeatherUrl = null,
    IReadOnlyCollection<int>? CategoryIds = null);
