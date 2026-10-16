using Microsoft.Data.Sqlite;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>Render-link cache with the same rules as the node store; a changed node makes its old picture just as wrong as its old JSON.</summary>
public sealed class SqliteRenderSnapshotStore(
    ISnapshotDatabase database,
    CacheOptions options,
    ILogger<SqliteRenderSnapshotStore> logger)
    : IRenderSnapshotStore
{
    private long _nextSweepAtTicks;

    /// <summary>Render link taken on the same file version; versions are compared only when both sides know them.</summary>
    public async Task<RenderSnapshot?> TryGetAsync(
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
                Log.RenderExpired(logger, fileKey, nodeId);

                return null;
            }

            if (currentFileVersion is not null && cached.FileVersion is not null && cached.FileVersion != currentFileVersion)
            {
                Log.RenderOutdated(logger, fileKey, nodeId, cached.FileVersion, currentFileVersion);

                return null;
            }

            return cached;
        }
        catch (SqliteException ex)
        {
            Log.RenderReadFailed(logger, ex);

            return null;
        }
    }

    public async Task SetAsync(RenderSnapshot image, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return;
        }

        try
        {
            image.LastFetchedAt = DateTimeOffset.UtcNow;
            image.ExpiresAt = image.LastFetchedAt.AddHours(options.ImageTtlHours);

            await using (var upsert = SnapshotSql.Command(
                connection,
                """
                INSERT OR REPLACE INTO "Renders" ("FileKey", "NodeId", "Variant", "FileVersion", "Url", "Fetched", "Expires")
                VALUES ($file, $node, $variant, $version, $url, $fetched, $expires);
                """,
                ("$file", image.FileKey),
                ("$node", image.NodeId),
                ("$variant", image.Variant),
                ("$version", image.FileVersion),
                ("$url", image.Url),
                ("$fetched", SnapshotSql.Milliseconds(image.LastFetchedAt)),
                ("$expires", SnapshotSql.Milliseconds(image.ExpiresAt))))
            {
                await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await SweepExpiredIfDueAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Log.RenderWriteFailed(logger, ex);
        }
    }

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
            var wanted = nodeIds.Select(NodeId.Normalize).ToHashSet(StringComparer.Ordinal);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var delete = SnapshotSql.Command(
                connection,
                """DELETE FROM "Renders" WHERE "FileKey" = $file AND "NodeId" = $node;""",
                ("$file", fileKey),
                ("$node", string.Empty));
            delete.Transaction = transaction;

            var removed = 0;

            foreach (var nodeId in wanted)
            {
                delete.Parameters["$node"].Value = nodeId;
                removed += await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return removed;
        }
        catch (SqliteException ex)
        {
            Log.RenderInvalidationFailed(logger, ex);

            return 0;
        }
    }

    /// <summary>Drops every render of the file — on <c>DOCUMENT_CHANGE</c> or a version change.</summary>
    public async Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return 0;
        }

        try
        {
            await using var delete = SnapshotSql.Command(connection, """DELETE FROM "Renders" WHERE "FileKey" = $file;""", ("$file", fileKey));
            var removed = await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (removed > 0)
            {
                Log.RenderCacheDropped(logger, fileKey, removed);
            }

            return removed;
        }
        catch (SqliteException ex)
        {
            Log.RenderCacheNotDropped(logger, ex);

            return 0;
        }
    }

    private static async Task<RenderSnapshot?> FindAsync(
        SqliteConnection connection,
        string fileKey,
        string nodeId,
        string variant,
        CancellationToken cancellationToken)
    {
        await using var select = SnapshotSql.Command(
            connection,
            """
            SELECT "FileVersion", "Url", "Fetched", "Expires"
            FROM "Renders" WHERE "FileKey" = $file AND "NodeId" = $node AND "Variant" = $variant;
            """,
            ("$file", fileKey),
            ("$node", nodeId),
            ("$variant", variant));
        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RenderSnapshot
        {
            FileKey = fileKey,
            NodeId = nodeId,
            Variant = variant,
            FileVersion = SnapshotSql.TextOrNull(reader, 0),
            Url = reader.GetString(1),
            LastFetchedAt = SnapshotSql.Moment(reader.GetInt64(2)),
            ExpiresAt = SnapshotSql.Moment(reader.GetInt64(3)),
        };
    }

    /// <summary>Sweeps expired entries at most once per <see cref="CacheOptions.ExpirationSweepSeconds"/>.</summary>
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

        await using var sweep = SnapshotSql.Command(connection, """DELETE FROM "Renders" WHERE "Expires" <= $now;""", ("$now", SnapshotSql.Milliseconds(now)));
        await sweep.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
