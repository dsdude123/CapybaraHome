using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CapybaraHome.Application.Homes;

/// <summary>
/// Reads the submitted tours one at a time, away from the request that submitted them. The queue
/// is the tours' own status, so a tour that was waiting when the app stopped is read after it starts.
/// </summary>
public class PropertyTourRunner(
    IServiceScopeFactory scopes,
    ILogger<PropertyTourRunner> logger,
    TimeProvider clock) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    /// <summary>Loops until shutdown, reading every waiting tour and then resting until the next sweep.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, clock);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var found = true;
                while (found)
                {
                    using var scope = scopes.CreateScope();
                    found = await scope.ServiceProvider.GetRequiredService<PropertyTourProcessor>()
                        .ProcessNextAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A tour sweep failed. The next one runs on schedule.");
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
