using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>One cache file per run in a temp directory: cache tests write to a real SQLite file, not a mock.</summary>
public sealed class SnapshotDatabaseFixture : IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(TemporaryFolder.Create(), "data");

    public ISnapshotDatabase Database { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var config = new ServerConfig
        {
            DataDirectory = _dataDirectory,
            Cache = { TtlHours = 1 },
        };

        var database = new SqliteSnapshotDatabase(config, NullLogger<SqliteSnapshotDatabase>.Instance);
        database.Open().Dispose();
        Database = database;

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(nameof(SnapshotDatabaseFixture))]
public sealed class SnapshotDatabaseCollection : ICollectionFixture<SnapshotDatabaseFixture>;
