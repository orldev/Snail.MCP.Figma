using Microsoft.Data.Sqlite;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>The snapshot cache as one SQLite file in the data directory, shared by every server process of the machine.</summary>
/// <remarks>
/// <para>
/// Written against Microsoft.Data.Sqlite directly, as Snail.MCP.Plans is. LiteDB came before it and mapped the snapshot
/// classes by reflection at run time, which a native build cannot do: an insert worked and a query with a method call in it
/// failed only when it ran. The file holds a cache and nothing else, so the old one is deleted rather than converted.
/// </para>
/// <para>WAL lets the processes of several clients read while one writes; a writer waits for another instead of failing.</para>
/// </remarks>
public sealed class SqliteSnapshotDatabase(ServerConfig config, ILogger<SqliteSnapshotDatabase> logger)
    : ISnapshotDatabase
{
    /// <summary>The file the LiteDB cache used; removed on first open.</summary>
    public const string LegacyFileName = "cache.db";

    private const string Schema =
        """
        CREATE TABLE IF NOT EXISTS "Nodes" (
            "FileKey" TEXT NOT NULL,
            "NodeId" TEXT NOT NULL,
            "Variant" TEXT NOT NULL,
            "FileVersion" TEXT NULL,
            "Fetched" INTEGER NOT NULL,
            "Expires" INTEGER NOT NULL,
            "Json" TEXT NOT NULL,
            "BytesIn" INTEGER NOT NULL,
            "BytesOut" INTEGER NOT NULL,
            PRIMARY KEY ("FileKey", "NodeId", "Variant")
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS "IX_Nodes_Expires" ON "Nodes" ("Expires");
        CREATE TABLE IF NOT EXISTS "Renders" (
            "FileKey" TEXT NOT NULL,
            "NodeId" TEXT NOT NULL,
            "Variant" TEXT NOT NULL,
            "FileVersion" TEXT NULL,
            "Url" TEXT NOT NULL,
            "Fetched" INTEGER NOT NULL,
            "Expires" INTEGER NOT NULL,
            PRIMARY KEY ("FileKey", "NodeId", "Variant")
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS "IX_Renders_Expires" ON "Renders" ("Expires");
        CREATE TABLE IF NOT EXISTS "Versions" (
            "FileKey" TEXT NOT NULL PRIMARY KEY,
            "Version" TEXT NULL,
            "LastModified" INTEGER NULL,
            "Name" TEXT NULL,
            "Checked" INTEGER NOT NULL
        );
        PRAGMA journal_mode = WAL;
        """;

    /// <summary>Pause before trying to open the file again after a failure.</summary>
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(1);

    /// <summary>Taken around every open: connections of one file are opened one at a time.</summary>
    /// <remarks>Microsoft.Data.Sqlite 10.0.11 and 10.0.12 mark a pooled connection active before naming its owner, and an open that finds the pool empty reclaims every active connection without an owner, so two callers can end up sharing one handle (dotnet/efcore#39008). The pool belongs to the process, so the lock does too; Snail.MCP.Plans met it first.</remarks>
    private static readonly Lock Opening = new();

    private readonly Lock _preparing = new();

    private readonly string _dataSource = new SqliteConnectionStringBuilder
    {
        DataSource = config.CacheDatabaseFile,
        Mode = SqliteOpenMode.ReadWriteCreate,
        DefaultTimeout = 5,
    }.ToString();

    private volatile bool _isPrepared;

    private DateTimeOffset _nextRetryAt = DateTimeOffset.MinValue;

    public bool IsStarted => _isPrepared;

    public string? LastError { get; private set; }

    public SqliteConnection Open()
    {
        if (!_isPrepared)
        {
            Prepare();
        }

        return Connect();
    }

    public SqliteConnection? TryOpen()
    {
        if (!config.Cache.Enabled || (!_isPrepared && DateTimeOffset.UtcNow < _nextRetryAt))
        {
            return null;
        }

        try
        {
            return Open();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Log.CacheUnavailable(logger, ex, _nextRetryAt);

            return null;
        }
    }

    public Task<SqliteConnection?> TryOpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(TryOpen());
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_dataSource);

        lock (Opening)
        {
            connection.Open();
        }

        return connection;
    }

    private void Prepare()
    {
        lock (_preparing)
        {
            if (_isPrepared)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(config.DataDirectory);
                RemoveOlderStores();
                Log.CacheOpening(logger, config.CacheDatabaseFile);
                var startedAt = DateTimeOffset.UtcNow;

                using (var connection = Connect())
                using (var schema = SnapshotSql.Command(connection, Schema))
                {
                    schema.ExecuteNonQuery();
                }

                _isPrepared = true;
                LastError = null;
                Log.CacheReady(logger, (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds, config.CacheDatabaseFile);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
                _nextRetryAt = DateTimeOffset.UtcNow + RetryCooldown;

                throw;
            }
        }
    }

    /// <summary>Removes the LiteDB file and the embedded RavenDB store before it, both caches of earlier versions.</summary>
    private void RemoveOlderStores()
    {
        var liteDb = Path.Combine(config.DataDirectory, LegacyFileName);
        var raven = config.LegacyRavenDirectory;

        try
        {
            if (File.Exists(liteDb) && !string.Equals(liteDb, config.CacheDatabaseFile, StringComparison.Ordinal))
            {
                File.Delete(liteDb);
                File.Delete(Path.Combine(config.DataDirectory, "cache-log.db"));
                Log.LegacyStoreRemoved(logger, liteDb);
            }

            if (Directory.Exists(raven) &&
                (Directory.Exists(Path.Combine(raven, "System")) || Directory.Exists(Path.Combine(raven, "Databases"))))
            {
                Directory.Delete(raven, recursive: true);
                Log.LegacyStoreRemoved(logger, raven);
            }

            var ravenLogs = Path.Combine(config.DataDirectory, "logs", "raven");

            if (Directory.Exists(ravenLogs))
            {
                Directory.Delete(ravenLogs, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LegacyStoreNotRemoved(logger, ex, config.DataDirectory);
        }
    }
}
