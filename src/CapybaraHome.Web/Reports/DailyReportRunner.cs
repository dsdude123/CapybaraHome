using CapybaraHome.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Web.Reports;

/// <summary>
/// Prints the report once a day while the app runs. The settings are read on every sweep rather
/// than held, so a time of day changed on the settings page takes effect the same morning, and
/// the sweep is a minute apart because the time is only ever picked to the minute.
/// </summary>
public class DailyReportRunner(
    IServiceScopeFactory scopes,
    ILogger<DailyReportRunner> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long after the chosen time the report may still be printed. Past that the morning is
    /// missed rather than printed late, which is what the script this came from did.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    /// <summary>Loops until shutdown, printing the report when its time of day has come round.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A daily report sweep failed. The next one runs on schedule.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Prints the report if its time has come round and it has not already printed today.</summary>
    public async Task SweepAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<DailyReportSettingsService>();
        var configured = await settings.LoadAsync(cancellationToken);

        if (!configured.IsEnabled)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var due = Instant(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).DateTime),
            configured.TimeOfDay,
            clock.LocalTimeZone);

        if (now < due || now - due > Grace || configured.LastRunAt >= due)
        {
            return;
        }

        var printing = scope.ServiceProvider.GetRequiredService<DailyReportPrinting>();

        try
        {
            await printing.PrintTodayAsync(cancellationToken);
            await settings.RecordRunAsync(now, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The daily report did not print. The next one runs on schedule.");
            await settings.RecordRunAsync(now, ex.Message, cancellationToken);
        }
    }

    /// <summary>
    /// Turns a day and a time of day into the instant it names. The hour the clocks go forward
    /// has no such wall time, so a time inside it moves to the first minute that does exist.
    /// </summary>
    public static DateTimeOffset Instant(DateOnly day, TimeOnly timeOfDay, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(timeOfDay);

        for (var minutes = 0; minutes < 24 * 60 && zone.IsInvalidTime(local); minutes++)
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
