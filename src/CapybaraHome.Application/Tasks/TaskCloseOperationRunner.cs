using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Sends the webhook calls that completed tasks have queued. The sweep is a quarter of a minute
/// apart so a call goes out while the person who closed the task is still looking at the page,
/// and it runs on startup as well so calls queued before a restart are not left behind.
/// </summary>
public class TaskCloseOperationRunner(
    IServiceScopeFactory scopes,
    ILogger<TaskCloseOperationRunner> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    /// <summary>Loops until shutdown, sending the queued webhook calls once per sweep.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var operations = scope.ServiceProvider.GetRequiredService<TaskCloseOperationService>();
                var summary = await operations.SendPendingWebhooksAsync(stoppingToken);

                if (summary.Attempted > 0)
                {
                    logger.LogInformation(
                        "Tried {Attempted} queued webhook call(s): {Succeeded} accepted, {Failed} given up on.",
                        summary.Attempted,
                        summary.Succeeded,
                        summary.Failed);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A webhook sweep failed. The next one runs on schedule.");
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
