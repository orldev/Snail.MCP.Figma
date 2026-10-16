using Microsoft.Data.Sqlite;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>Remembers the last known file version taken from the read envelope, which costs no extra request.</summary>
public sealed class SqliteDocumentVersionLedger(
    ISnapshotDatabase database,
    INodeSnapshotStore cache,
    IRenderSnapshotStore images,
    ILogger<SqliteDocumentVersionLedger> logger)
    : IDocumentVersionLedger
{
    /// <summary>Records the version that came with read data; a change drops the file's other snapshots, so call it before caching the fresh one.</summary>
    public async Task RecordAsync(string fileKey, DocumentVersion info, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return;
        }

        try
        {
            var stored = Find(connection, fileKey);

            if (stored?.Version is not null && info.Version is not null && stored.Version != info.Version)
            {
                Log.FileChanged(logger, fileKey, stored.Version, info.Version);
                await cache.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
                await images.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
            }

            await using var upsert = SnapshotSql.Command(
                connection,
                """
                INSERT OR REPLACE INTO "Versions" ("FileKey", "Version", "LastModified", "Name", "Checked")
                VALUES ($file, $version, $modified, $name, $checked);
                """,
                ("$file", fileKey),
                ("$version", info.Version),
                ("$modified", info.LastModified is { } modified ? SnapshotSql.Milliseconds(modified) : null),
                ("$name", info.Name),
                ("$checked", SnapshotSql.Milliseconds(DateTimeOffset.UtcNow)));
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Log.VersionNotStored(logger, ex, fileKey);
        }
    }

    /// <summary>Last remembered version — for diagnostics and tool responses.</summary>
    public DocumentVersionRecord? GetStored(string fileKey)
    {
        using var connection = database.TryOpen();

        if (connection is null)
        {
            return null;
        }

        try
        {
            return Find(connection, fileKey);
        }
        catch (SqliteException ex)
        {
            Log.VersionNotRead(logger, ex, fileKey);

            return null;
        }
    }

    /// <summary>Forgets the remembered version; after a plugin mutation it must be re-read.</summary>
    public async Task ForgetAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.TryOpenAsync(cancellationToken).ConfigureAwait(false);

        if (connection is null)
        {
            return;
        }

        try
        {
            await using var delete = SnapshotSql.Command(connection, """DELETE FROM "Versions" WHERE "FileKey" = $file;""", ("$file", fileKey));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            Log.VersionNotForgotten(logger, ex, fileKey);
        }
    }

    /// <summary>The stored version, read synchronously: <see cref="GetStored"/> is synchronous in its port, and one indexed row costs microseconds.</summary>
    private static DocumentVersionRecord? Find(SqliteConnection connection, string fileKey)
    {
        using var select = SnapshotSql.Command(
            connection,
            """SELECT "Version", "LastModified", "Name", "Checked" FROM "Versions" WHERE "FileKey" = $file;""",
            ("$file", fileKey));
        using var reader = select.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new DocumentVersionRecord
        {
            FileKey = fileKey,
            Version = SnapshotSql.TextOrNull(reader, 0),
            LastModified = reader.IsDBNull(1) ? null : SnapshotSql.Moment(reader.GetInt64(1)),
            Name = SnapshotSql.TextOrNull(reader, 2),
            CheckedAt = SnapshotSql.Moment(reader.GetInt64(3)),
        };
    }
}
