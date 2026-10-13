using Microsoft.Data.Sqlite;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>Sanitized node cache; every database failure is swallowed because the cache is an optimization, not a dependency.</summary>
public sealed class SqliteNodeSnapshotStore(
    ISnapshotDatabase database,
    CacheOptions options,
    ILogger<SqliteNodeSnapshotStore> logger)
    : INodeSnapshotStore
{
    /// <summary>When expired entries may be swept again; reads filter by expiry anyway.</summary>
    private long _nextSweepAtTicks;

    /// <summary>Returns the snapshot taken on the same file version; a version mismatch is a miss.</summary>
    public async Task<NodeSnapshot?> TryGetAsync(
        string fileKey,
        string nodeId,
        string variant,
        string? currentFileVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return null;
        }

        try
        {
            var cached = await FindAsync(connection, fileKey, nodeId, variant, cancellationToken).ConfigureAwait(false);

            if (cached is null)
            {
                return null;
            }

            if (cached.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                Log.SnapshotExpired(logger, fileKey, nodeId, cached.ExpiresAt);

                return null;
            }

            if (currentFileVersion is not null && cached.FileVersion != currentFileVersion)
            {
                Log.SnapshotOutdated(logger, fileKey, nodeId, cached.FileVersion, currentFileVersion);

                return null;
            }

            return cached;
        }
        catch (SqliteException ex)
        {
            Log.CacheReadFailed(logger, ex);

            return null;
        }
    }

    /// <summary>Stores a snapshot with a safety TTL.</summary>
    public async Task SetAsync(NodeSnapshot node, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return;
        }

        try
        {
            node.LastFetchedAt = DateTimeOffset.UtcNow;
            node.ExpiresAt = node.LastFetchedAt.AddHours(options.TtlHours);

            await using (var upsert = SnapshotSql.Command(
                connection,
                """
                INSERT OR REPLACE INTO "Nodes" ("FileKey", "NodeId", "Variant", "FileVersion", "Fetched", "Expires", "Json", "BytesIn", "BytesOut")
                VALUES ($file, $node, $variant, $version, $fetched, $expires, $json, $in, $out);
                """,
                ("$file", node.FileKey),
                ("$node", node.NodeId),
                ("$variant", node.Variant),
                ("$version", node.FileVersion),
                ("$fetched", SnapshotSql.Milliseconds(node.LastFetchedAt)),
                ("$expires", SnapshotSql.Milliseconds(node.ExpiresAt)),
                ("$json", node.OptimizedJson),
                ("$in", node.BytesIn),
                ("$out", node.BytesOut)))
            {
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SweepExpiredIfDueAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Log.CacheWriteFailed(logger, ex);
        }
    }

    /// <summary>Invalidation after a write; callers must pass ancestors too, since Auto Layout reflows upwards.</summary>
    public async Task<int> InvalidateNodesAsync(
        string fileKey,
        IReadOnlyCollection<string> nodeIds,
        CancellationToken cancellationToken = default)
    {
        if (nodeIds.Count == 0)
        {
            return 0;
        }

        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return 0;
        }

        try
        {
            var removed = 0;

            foreach (var nodeId in nodeIds.Select(NodeId.Normalize).ToHashSet(StringComparer.Ordinal))
            {
                removed += await DeleteAsync(connection, fileKey, nodeId, cancellationToken).ConfigureAwait(false);
            }

            await DeleteAsync(connection, fileKey, NodeSnapshot.WholeFileNodeId, cancellationToken).ConfigureAwait(false);

            return removed;
        }
        catch (SqliteException ex)
        {
            Log.NodeInvalidationFailed(logger, ex);

            return 0;
        }
    }

    /// <summary>Drops every snapshot of the file — on <c>DOCUMENT_CHANGE</c> or a version change.</summary>
    public async Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return 0;
        }

        try
        {
            await using var delete = SnapshotSql.Command(connection, """DELETE FROM "Nodes" WHERE "FileKey" = $file;""", ("$file", fileKey));
            var removed = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Log.FileCacheDropped(logger, fileKey, removed);

            return removed;
        }
        catch (SqliteException ex)
        {
            Log.FileCacheNotDropped(logger, ex);

            return 0;
        }
    }

    private static async Task<NodeSnapshot?> FindAsync(
        SqliteConnection connection,
        string fileKey,
        string nodeId,
        string variant,
        CancellationToken cancellationToken)
    {
        await using var select = SnapshotSql.Command(
            connection,
            """
            SELECT "FileVersion", "Fetched", "Expires", "Json", "BytesIn", "BytesOut"
            FROM "Nodes" WHERE "FileKey" = $file AND "NodeId" = $node AND "Variant" = $variant;
            """,
            ("$file", fileKey),
            ("$node", nodeId),
            ("$variant", variant));
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new NodeSnapshot
        {
            FileKey = fileKey,
            NodeId = nodeId,
            Variant = variant,
            FileVersion = SnapshotSql.TextOrNull(reader, 0),
            LastFetchedAt = SnapshotSql.Moment(reader.GetInt64(1)),
            ExpiresAt = SnapshotSql.Moment(reader.GetInt64(2)),
            OptimizedJson = reader.GetString(3),
            BytesIn = reader.GetInt64(4),
            BytesOut = reader.GetInt64(5),
        };
    }

    private static async Task<int> DeleteAsync(SqliteConnection connection, string fileKey, string nodeId, CancellationToken cancellationToken)
    {
        await using var delete = SnapshotSql.Command(
            connection,
            """DELETE FROM "Nodes" WHERE "FileKey" = $file AND "NodeId" = $node;""",
            ("$file", fileKey),
            ("$node", nodeId));

        return await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sweeps expired entries at most once per <see cref="CacheOptions.ExpirationSweepSeconds"/>; reads filter by expiry anyway.</summary>
    private async Task SweepExpiredIfDueAsync(SqliteConnection connection, CancellationToken cancellationToken)
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

        await using var sweep = SnapshotSql.Command(connection, """DELETE FROM "Nodes" WHERE "Expires" <= $now;""", ("$now", SnapshotSql.Milliseconds(now)));
        var removed = await sweep.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (removed > 0)
        {
            Log.ExpiredSwept(logger, removed);
        }
    }
}
