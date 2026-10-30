using CapybaraHome.Application.Reports;

namespace CapybaraHome.Web.Reports;

/// <summary>Lays today's report out and sends it to the printer the settings name.</summary>
public class DailyReportPrinting(
    DailyReportService reports,
    DailyReportSettingsService settings,
    IppPrinter printer,
    ILogger<DailyReportPrinting> logger)
{
    /// <summary>What the job is called in the printer's queue.</summary>
    private const string JobName = "Morning Report";

    /// <summary>Gathers today's report.</summary>
    public Task<DailyReport> BuildAsync(CancellationToken cancellationToken = default) =>
        reports.BuildAsync(cancellationToken);

    /// <summary>Returns how the report is set up to be built and printed.</summary>
    public Task<DailyReportSettingsView> SettingsAsync(CancellationToken cancellationToken = default) =>
        settings.GetAsync(cancellationToken);

    /// <summary>Prints a report that has already been gathered.</summary>
    /// <exception cref="InvalidOperationException">No printer is configured, or the printer refused the job.</exception>
    public async Task PrintAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        var configured = await settings.LoadAsync(cancellationToken);

        await printer.PrintAsync(
            configured.PrinterHost,
            configured.PrinterPort,
            configured.PrinterQueue,
            DailyReportPdf.Render(report),
            JobName,
            configured.PrintBothSides,
            cancellationToken);

        logger.LogInformation("Printed the daily report on {Printer}.", configured.PrinterHost);
    }

    /// <summary>Gathers today's report and prints it.</summary>
    /// <exception cref="InvalidOperationException">No printer is configured, or the printer refused the job.</exception>
    public async Task PrintTodayAsync(CancellationToken cancellationToken = default)
    {
        var report = await BuildAsync(cancellationToken);
        await PrintAsync(report, cancellationToken);
    }
}
