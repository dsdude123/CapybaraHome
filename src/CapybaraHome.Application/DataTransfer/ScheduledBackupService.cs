using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CapybaraHome.Application.DataTransfer;

/// <summary>Writes a backup on a timer while the app runs, when backups are turned on.</summary>
public class ScheduledBackupService(
    IServiceScopeFactory scopes,
    IOptions<BackupOptions> options,
    ILogger<ScheduledBackupService> logger,
    TimeProvider clock) : BackgroundService
{
    /// <summary>Loops until shutdown, writing one backup per configured interval.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Automatic backups are off.");
            return;
        }

        var interval = settings.Interval > TimeSpan.Zero ? settings.Interval : TimeSpan.FromHours(24);
        logger.LogInformation("Automatic backups every {Interval} to {Destination}.", interval, settings.Destination);

        using var timer = new PeriodicTimer(interval, clock);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var writer = scope.ServiceProvider.GetRequiredService<BackupWriter>();
                var path = await writer.WriteAsync(stoppingToken);
                logger.LogInformation("Wrote a backup to {Path}.", path);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The backup failed. The next one runs on schedule.");
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
}
