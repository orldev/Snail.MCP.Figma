using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Persistence;

/// <summary>The cache is opened when the server starts, before any call needs it.</summary>
public sealed class SnapshotCacheWarmUpTests : IDisposable
{
    private readonly string _directory = TemporaryFolder.Create();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task SnapshotCacheWarmUp_HostStarts_OpensTheCacheBeforeAnyCall()
    {
        var database = new SqliteSnapshotDatabase(new ServerConfig { DataDirectory = _directory }, NullLogger<SqliteSnapshotDatabase>.Instance);
        using var warmUp = new SnapshotCacheWarmUp(database, NullLogger<SnapshotCacheWarmUp>.Instance);

        await warmUp.StartAsync(CancellationToken.None);

        await BridgeHostFixture.WaitUntilAsync(() => database.IsStarted, "the cache was not opened at start");
        await warmUp.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SnapshotCacheWarmUp_CacheDisabled_LeavesItClosed()
    {
        var database = new SqliteSnapshotDatabase(new ServerConfig { DataDirectory = _directory, Cache = { Enabled = false } }, NullLogger<SqliteSnapshotDatabase>.Instance);
        using var warmUp = new SnapshotCacheWarmUp(database, NullLogger<SnapshotCacheWarmUp>.Instance);

        await warmUp.StartAsync(CancellationToken.None);
        await Task.Delay(200);
        await warmUp.StopAsync(CancellationToken.None);

        Assert.False(database.IsStarted);
    }
}
