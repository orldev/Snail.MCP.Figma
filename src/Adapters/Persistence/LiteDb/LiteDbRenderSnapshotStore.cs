using LiteDB;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Render-link cache with the same rules as the node store; a changed node makes its old picture just as wrong as its old JSON.</summary>
public sealed class LiteDbRenderSnapshotStore(
    ILiteDatabaseProvider databaseProvider,
    CacheOptions options,
    ILogger<LiteDbRenderSnapshotStore> logger)
    : IRenderSnapshotStore
{
    private long _nextSweepAtTicks;

    /// <summary>Render link taken on the same file version; versions are compared only when both sides know them.</summary>
    public Task<RenderSnapshot?> TryGetAsync(
        string fileKey,
        string nodeId,
        string variant,
        string? currentFileVersion,
        CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.FromResult<RenderSnapshot?>(null);
        }

        try
        {
            var cached = Images(database).FindById(LiteDbCollections.ImageKey(fileKey, nodeId, variant));

            if (cached is null)
            {
                return Task.FromResult<RenderSnapshot?>(null);
            }

            if (cached.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Log.RenderExpired(logger, fileKey, nodeId);

                return Task.FromResult<RenderSnapshot?>(null);
            }

            if (currentFileVersion is not null && cached.FileVersion is not null && cached.FileVersion != currentFileVersion)
            {
                Log.RenderOutdated(logger, fileKey, nodeId, cached.FileVersion, currentFileVersion);

                return Task.FromResult<RenderSnapshot?>(null);
            }

            return Task.FromResult<RenderSnapshot?>(cached);
        }
        catch (Exception ex)
        {
            Log.RenderReadFailed(logger, ex);

            return Task.FromResult<RenderSnapshot?>(null);
        }
    }

    public Task SetAsync(RenderSnapshot image, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            image.Id = LiteDbCollections.ImageKey(image.FileKey, image.NodeId, image.Variant);
            image.LastFetchedAt = DateTimeOffset.UtcNow;
            image.ExpiresAt = image.LastFetchedAt.AddHours(options.ImageTtlHours);

            Images(database).Upsert(image);

            SweepExpiredIfDue(database);
        }
        catch (Exception ex)
        {
            Log.RenderWriteFailed(logger, ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>Drops renders of specific nodes after a plugin mutation.</summary>
    public Task<int> InvalidateNodesAsync(
        string fileKey,
        IReadOnlyCollection<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        if (nodeIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.FromResult(0);
        }

        var normalized = nodeIds.Select(NodeId.Normalize).ToHashSet(StringComparer.Ordinal);

        try
        {
            var images = Images(database);
            var removed = 0;

            foreach (var nodeId in normalized)
            {
                removed += images.DeleteMany(i => i.FileKey == fileKey && i.NodeId == nodeId);
            }

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            Log.RenderInvalidationFailed(logger, ex);

            return Task.FromResult(0);
        }
    }

    /// <summary>Drops every render of the file — on <c>DOCUMENT_CHANGE</c> or a version change.</summary>
    public Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.FromResult(0);
        }

        try
        {
            var removed = Images(database).DeleteMany(i => i.FileKey == fileKey);

            if (removed > 0)
            {
                Log.RenderCacheDropped(logger, fileKey, removed);
            }

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            Log.RenderCacheNotDropped(logger, ex);

            return Task.FromResult(0);
        }
    }

    /// <summary>Sweeps expired entries at most once per <see cref="CacheOptions.ExpirationSweepSeconds"/>.</summary>
    private void SweepExpiredIfDue(LiteDatabase database)
    {
        var now = DateTimeOffset.UtcNow;
        var due = Interlocked.Read(ref _nextSweepAtTicks);

        if (now.UtcTicks < due)
        {
            return;
        }

        var next = now.AddSeconds(Math.Max(1, options.ExpirationSweepSeconds)).UtcTicks;

        if (Interlocked.CompareExchange(ref _nextSweepAtTicks, next, due) != due)
        {
            return;
        }

        Images(database).DeleteMany(i => i.ExpiresAt <= now);
    }

    private static ILiteCollection<RenderSnapshot> Images(LiteDatabase database) =>
        database.GetCollection<RenderSnapshot>(LiteDbCollections.Images);
}
