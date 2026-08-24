using LiteDB;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Sanitized node cache; every database failure is swallowed because the cache is an optimization, not a dependency.</summary>
public sealed class LiteDbNodeSnapshotStore(
    ILiteDatabaseProvider databaseProvider,
    CacheOptions options,
    ILogger<LiteDbNodeSnapshotStore> logger)
    : INodeSnapshotStore
{
    /// <summary>When expired entries may be swept again; LiteDB has no expiration bundle.</summary>
    private long _nextSweepAtTicks;

    /// <summary>Returns the snapshot taken on the same file version; a version mismatch is a miss.</summary>
    public Task<NodeSnapshot?> TryGetAsync(
        string fileKey,
        string nodeId,
        string variant,
        string? currentFileVersion,
        CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.FromResult<NodeSnapshot?>(null);
        }

        try
        {
            var cached = Nodes(database).FindById(LiteDbCollections.NodeKey(fileKey, nodeId, variant));

            if (cached is null)
            {
                return Task.FromResult<NodeSnapshot?>(null);
            }

            if (cached.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                logger.LogDebug("Cache {FileKey}/{NodeId} expired ({ExpiresAt:O}) — miss", fileKey, nodeId, cached.ExpiresAt);

                return Task.FromResult<NodeSnapshot?>(null);
            }

            if (currentFileVersion is not null && cached.FileVersion != currentFileVersion)
            {
                logger.LogDebug(
                    "Cache {FileKey}/{NodeId} was taken on version {Cached}, current is {Current} — miss",
                    fileKey, nodeId, cached.FileVersion, currentFileVersion);

                return Task.FromResult<NodeSnapshot?>(null);
            }

            return Task.FromResult<NodeSnapshot?>(cached);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache read failed, falling back to Figma");

            return Task.FromResult<NodeSnapshot?>(null);
        }
    }

    /// <summary>Stores a snapshot with a safety TTL.</summary>
    public Task SetAsync(NodeSnapshot node, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            node.Id = LiteDbCollections.NodeKey(node.FileKey, node.NodeId, node.Variant);
            node.LastFetchedAt = DateTimeOffset.UtcNow;
            node.ExpiresAt = node.LastFetchedAt.AddHours(options.TtlHours);

            Nodes(database).Upsert(node);

            SweepExpiredIfDue(database);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed, continuing without caching");
        }

        return Task.CompletedTask;
    }

    /// <summary>Invalidation after a write; callers must pass ancestors too, since Auto Layout reflows upwards.</summary>
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
            var nodes = Nodes(database);
            var removed = normalized.Sum(nodeId => nodes.DeleteMany(n => n.FileKey == fileKey && n.NodeId == nodeId));

            nodes.DeleteMany(n => n.FileKey == fileKey && n.NodeId == NodeSnapshot.WholeFileNodeId);

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Node invalidation failed");

            return Task.FromResult(0);
        }
    }

    /// <summary>Drops every snapshot of the file — on <c>DOCUMENT_CHANGE</c> or a version change.</summary>
    public Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var database = databaseProvider.TryGetDatabase();

        if (database is null)
        {
            return Task.FromResult(0);
        }

        try
        {
            var removed = Nodes(database).DeleteMany(n => n.FileKey == fileKey);
            logger.LogInformation("Cache of file {FileKey} dropped, {Count} documents removed", fileKey, removed);

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dropping the file cache failed");

            return Task.FromResult(0);
        }
    }

    /// <summary>Sweeps expired entries at most once per <see cref="CacheOptions.ExpirationSweepSeconds"/>; reads filter by <c>ExpiresAt</c> anyway.</summary>
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

        var removed = Nodes(database).DeleteMany(n => n.ExpiresAt <= now);

        if (removed > 0)
        {
            logger.LogDebug("Swept {Count} expired snapshots", removed);
        }
    }

    private static ILiteCollection<NodeSnapshot> Nodes(LiteDatabase database) =>
        database.GetCollection<NodeSnapshot>(LiteDbCollections.Nodes);
}
