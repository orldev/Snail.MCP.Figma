using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Persistence;

/// <summary>The cache has no expiry of its own, so our code sweeps; <see cref="SqliteNodeSnapshotStore.SetAsync"/> swallows exceptions and a broken sweep would silently bloat the file forever.</summary>
[Collection(nameof(SnapshotDatabaseFixture))]
public class SqliteNodeSnapshotStoreTests(SnapshotDatabaseFixture cacheFixture)
{
    private readonly SnapshotDatabaseFixture _cacheFixture = cacheFixture;
    private readonly string _fileKey = $"sweep-{Guid.NewGuid().ToString("N")[..8]}";

    private SqliteNodeSnapshotStore BuildCache(int ttlHours) =>
        new(_cacheFixture.Database,
            new CacheOptions { Enabled = true, TtlHours = ttlHours },
            NullLogger<SqliteNodeSnapshotStore>.Instance);

    private NodeSnapshot Slice(string nodeId) => new()
    {
        FileKey = _fileKey,
        NodeId = nodeId,
        Variant = "test",
        FileVersion = "v1",
        OptimizedJson = "{}",
    };

    private int Count()
    {
        using var connection = _cacheFixture.Database.Open();
        using var count = connection.CreateCommand();
        count.CommandText = """SELECT COUNT(*) FROM "Nodes" WHERE "FileKey" = $file;""";
        count.Parameters.AddWithValue("$file", _fileKey);

        return Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Writes a slice that expired an hour ago straight into the file, past the store, which would sweep it at once.</summary>
    private void InsertExpired(string nodeId)
    {
        using var connection = _cacheFixture.Database.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO "Nodes" ("FileKey", "NodeId", "Variant", "FileVersion", "Fetched", "Expires", "Json", "BytesIn", "BytesOut")
            VALUES ($file, $node, 'test', 'v1', $fetched, $expires, '{}', 0, 0);
            """;
        insert.Parameters.AddWithValue("$file", _fileKey);
        insert.Parameters.AddWithValue("$node", nodeId);
        insert.Parameters.AddWithValue("$fetched", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds());
        insert.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds());
        insert.ExecuteNonQuery();
    }

    [Fact]
    public async Task SqliteNodeSnapshotStore_ExpiredSlices_AreSweptOnTheNextWrite()
    {
        InsertExpired("1:1");
        Assert.Equal(1, Count());

        await BuildCache(ttlHours: 1).SetAsync(Slice("2:2"));

        Assert.Equal(1, Count());
        Assert.NotNull(await BuildCache(ttlHours: 1).TryGetAsync(_fileKey, "2:2", "test", "v1"));
        Assert.Null(await BuildCache(ttlHours: 1).TryGetAsync(_fileKey, "1:1", "test", "v1"));
    }

    [Fact]
    public async Task SqliteNodeSnapshotStore_LiveSlices_SurviveTheSweep()
    {
        await BuildCache(ttlHours: 1).SetAsync(Slice("3:3"));
        await BuildCache(ttlHours: 1).SetAsync(Slice("4:4"));

        Assert.Equal(2, Count());
    }

    [Fact]
    public async Task SqliteNodeSnapshotStore_SliceStored_ComesBackAsItWas()
    {
        var stored = Slice("5:5");
        stored.BytesIn = 1200;
        stored.BytesOut = 300;
        stored.OptimizedJson = """{"name":"Header"}""";
        await BuildCache(ttlHours: 1).SetAsync(stored);

        var read = await BuildCache(ttlHours: 1).TryGetAsync(_fileKey, "5:5", "test", "v1");

        Assert.NotNull(read);
        Assert.Equal("""{"name":"Header"}""", read.OptimizedJson);
        Assert.Equal(1200, read.BytesIn);
        Assert.Equal(300, read.BytesOut);
        Assert.Null(await BuildCache(ttlHours: 1).TryGetAsync(_fileKey, "5:5", "test", "v2"));
    }
}
