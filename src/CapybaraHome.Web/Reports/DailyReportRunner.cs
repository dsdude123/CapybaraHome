using CapybaraHome.Application.Reports;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Web.Reports;

/// <summary>
/// Prints the report once a day while the app runs. The wait is to the next time of day rather
/// than a poll, so a day whose printing time passed while the app was down is simply missed, the
/// way a morning paper is: tomorrow's report is the one worth having.
/// </summary>
public class DailyReportRunner(
    IServiceScopeFactory scopes,
    IOptions<DailyReportOptions> options,
    ILogger<DailyReportRunner> logger,
    TimeProvider clock) : BackgroundService
{
    /// <summary>Loops until shutdown, printing the report at the configured time of day.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The daily report is not printed on a schedule.");
            return;
        }

        var zone = clock.LocalTimeZone;
        logger.LogInformation(
            "The daily report prints at {Time} {Zone} on {Printer}.",
            settings.TimeOfDay,
            zone.Id,
            settings.PrinterHost ?? "no printer");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = NextRun(now, settings.TimeOfDay, zone);

            try
            {
                await Task.Delay(next - now, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var scope = scopes.CreateScope();
                var printing = scope.ServiceProvider.GetRequiredService<DailyReportPrinting>();
                await printing.PrintTodayAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The daily report did not print. The next one runs on schedule.");
            }
        }
    }

    /// <summary>
    /// Returns the next instant <paramref name="timeOfDay"/> comes round in <paramref name="zone"/>.
    /// The hour the clocks go forward has no such wall time, so a time inside it moves to the first
    /// minute that does exist.
    /// </summary>
    public static DateTimeOffset NextRun(DateTimeOffset after, TimeOnly timeOfDay, TimeZoneInfo zone)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);

        for (var ahead = 0; ahead < 3; ahead++)
        {
            var candidate = Instant(day.AddDays(ahead), timeOfDay, zone);
            if (candidate > after)
            {
                return candidate;
            }
        }

        return Instant(day.AddDays(3), timeOfDay, zone);
    }

    private static DateTimeOffset Instant(DateOnly day, TimeOnly timeOfDay, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(timeOfDay);

        for (var minutes = 0; minutes < 24 * 60 && zone.IsInvalidTime(local); minutes++)
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
