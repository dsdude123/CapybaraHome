using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Sweeps the automations while the app runs, creating the tasks whose execution time has passed.
/// The sweep is a minute apart because an execution time is only ever picked to the minute, and it
/// happens once on startup as well so a restart that straddled one is caught up at once.
/// </summary>
public class TaskAutomationRunner(
    IServiceScopeFactory scopes,
    ILogger<TaskAutomationRunner> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>Loops until shutdown, running the due automations once per sweep.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var automations = scope.ServiceProvider.GetRequiredService<TaskAutomationService>();
                var summary = await automations.RunDueAsync(stoppingToken);

                if (summary.Ran > 0)
                {
                    logger.LogInformation(
                        "Ran {Ran} automation(s): {Created} task(s) created, {Skipped} skipped because one was already open.",
                        summary.Ran,
                        summary.TasksCreated,
                        summary.Skipped);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An automation sweep failed. The next one runs on schedule.");
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
