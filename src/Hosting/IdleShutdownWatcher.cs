using Microsoft.Extensions.Hosting;

namespace Snail.MCP.Figma.Hosting;

/// <summary>Stops the host when no MCP tool has been called for longer than the idle timeout and no plugin is connected.</summary>
public sealed class IdleShutdownWatcher(
    IActivityTracker activity,
    IOpenDocuments documents,
    IdleShutdownOptions options,
    IHostApplicationLifetime appLifetime,
    TimeProvider timeProvider,
    ILogger<IdleShutdownWatcher> logger)
    : BackgroundService
{
    /// <summary>Checks on every poll whether the idle timeout has passed with no plugin connected, and stops the host when it has.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.PollInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {

                if (documents.HasConnection)
                {
                    activity.Touch();

                    continue;
                }

                var idleFor = timeProvider.GetUtcNow().UtcDateTime - activity.LastActivityUtc;

                if (idleFor < options.IdleTimeout) continue;

                Log.IdleShutdown(logger, idleFor);
                appLifetime.StopApplication();

                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
