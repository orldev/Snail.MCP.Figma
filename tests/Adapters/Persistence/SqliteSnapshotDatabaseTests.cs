using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Persistence;

/// <summary>The cache file of earlier versions is a cache too: it is removed on first open, not converted.</summary>
public sealed class SqliteSnapshotDatabaseTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("snail-snapshots-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void TheLiteDbFileOfAnEarlierVersionIsRemovedOnFirstOpen()
    {
        var legacy = Path.Combine(_directory, SqliteSnapshotDatabase.LegacyFileName);
        File.WriteAllText(legacy, "LiteDB");
        var config = new ServerConfig { DataDirectory = _directory };
        var database = new SqliteSnapshotDatabase(config, NullLogger<SqliteSnapshotDatabase>.Instance);

        database.Open().Dispose();

        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(config.CacheDatabaseFile));
        Assert.True(database.IsStarted);
    }

    [Fact]
    public void ADisabledCacheOpensNothing()
    {
        var config = new ServerConfig { DataDirectory = _directory, Cache = { Enabled = false } };
        var database = new SqliteSnapshotDatabase(config, NullLogger<SqliteSnapshotDatabase>.Instance);

        Assert.Null(database.TryOpen());
        Assert.False(File.Exists(config.CacheDatabaseFile));
    }
}
