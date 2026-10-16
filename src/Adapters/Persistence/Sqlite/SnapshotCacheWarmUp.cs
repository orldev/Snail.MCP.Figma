using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>Opens the cache in the background as soon as the server starts, so no agent's call waits for it.</summary>
/// <remarks>
/// The first open of a publication's SQLite library costs up to a second: macOS looks into a native library the first
/// time it is loaded, and every install brings a fresh copy of it. Measured at 325–900 ms against 1 ms for every load
/// after it; the schema and the journal mode cost a few milliseconds. Opened here, that second is spent while nobody
/// waits; a call that arrives meanwhile waits for the same open rather than starting a second one.
/// </remarks>
public sealed class SnapshotCacheWarmUp(ISnapshotDatabase database, ILogger<SnapshotCacheWarmUp> logger) : BackgroundService
{
    /// <summary>Opens the cache once right after startup so that the first slow open is paid while nobody waits.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var startedAt = Stopwatch.GetTimestamp();

        using (var connection = database.TryOpen())
        {
            if (connection is null)
            {
                return;
            }
        }

        Log.CacheWarmedUp(logger, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }
}
