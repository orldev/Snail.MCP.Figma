using Microsoft.Data.Sqlite;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary><see cref="ISnapshotStoreHealth"/> over the SQLite cache file.</summary>
public sealed class SqliteStoreHealth(
    ISnapshotDatabase database,
    ILogger<SqliteStoreHealth> logger)
    : ISnapshotStoreHealth
{
    public bool IsStarted => database.IsStarted;

    public string? LastError => database.LastError;

    public SnapshotStoreProbe Probe()
    {
        try
        {
            using var connection = database.Open();

            return new SnapshotStoreProbe(
                Succeeded: true,
                NodeEntries: Count(connection, "Nodes"),
                ImageEntries: Count(connection, "Renders"),
                Error: null);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Log.CacheNotOpened(logger, ex);

            return SnapshotStoreProbe.Failed(ex.Message);
        }
    }

    private static int Count(SqliteConnection connection, string table)
    {
        using var count = SnapshotSql.Command(connection, $"""SELECT COUNT(*) FROM "{table}";""");

        return Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
