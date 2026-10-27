using CapybaraHome.Application.Reports;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Web.Reports;

/// <summary>Lays today's report out and sends it to the printer.</summary>
public class DailyReportPrinting(
    DailyReportService reports,
    IppPrinter printer,
    IOptions<DailyReportOptions> options,
    ILogger<DailyReportPrinting> logger)
{
    /// <summary>What the job is called in the printer's queue.</summary>
    private const string JobName = "Morning Report";

    /// <summary>Whether a printer is configured at all.</summary>
    public bool HasPrinter => !string.IsNullOrWhiteSpace(options.Value.PrinterHost);

    /// <summary>The printer the report goes to, for the page to name.</summary>
    public string? PrinterHost => options.Value.PrinterHost;

    /// <summary>Gathers today's report.</summary>
    public Task<DailyReport> BuildAsync(CancellationToken cancellationToken = default) =>
        reports.BuildAsync(cancellationToken);

    /// <summary>Prints a report that has already been gathered.</summary>
    /// <exception cref="InvalidOperationException">No printer is configured, or the printer refused the job.</exception>
    public async Task PrintAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        await printer.PrintAsync(
            settings.PrinterHost,
            settings.PrinterPort,
            settings.PrinterResource,
            DailyReportPdf.Render(report),
            JobName,
            cancellationToken);

        logger.LogInformation("Printed the daily report on {Printer}.", settings.PrinterHost);
    }

    /// <summary>Gathers today's report and prints it.</summary>
    /// <exception cref="InvalidOperationException">No printer is configured, or the printer refused the job.</exception>
    public async Task PrintTodayAsync(CancellationToken cancellationToken = default)
    {
        var report = await BuildAsync(cancellationToken);
        await PrintAsync(report, cancellationToken);
    }
}
