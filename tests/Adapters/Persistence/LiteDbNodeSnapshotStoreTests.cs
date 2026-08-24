using LiteDB;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Tests.Adapters.Persistence;

/// <summary>LiteDB has no expiration bundle, so our code sweeps; <see cref="LiteDbNodeSnapshotStore.SetAsync"/> swallows exceptions and a broken sweep would silently bloat the file forever.</summary>
[Collection(nameof(LiteDbFixture))]
public class LiteDbNodeSnapshotStoreTests
{
    private readonly LiteDbFixture _cacheFixture;
    private readonly string _fileKey = $"sweep-{Guid.NewGuid().ToString("N")[..8]}";

    public LiteDbNodeSnapshotStoreTests(LiteDbFixture cacheFixture) => _cacheFixture = cacheFixture;

    private LiteDbNodeSnapshotStore BuildCache(int ttlHours) =>
        new(_cacheFixture.Database,
            new CacheOptions { Enabled = true, TtlHours = ttlHours },
            NullLogger<LiteDbNodeSnapshotStore>.Instance);

    private NodeSnapshot Slice(string nodeId) => new()
    {
        FileKey = _fileKey,
        NodeId = nodeId,
        Variant = "test",
        FileVersion = "v1",
        OptimizedJson = "{}",
    };

    private ILiteCollection<NodeSnapshot> Nodes() =>
        _cacheFixture.Database.GetDatabase().GetCollection<NodeSnapshot>(LiteDbCollections.Nodes);

    [Fact]
    public async Task ExpiredSlicesAreSweptOnTheNextWrite()
    {
        var expired = Slice("1:1");
        expired.Id = LiteDbCollections.NodeKey(expired.FileKey, expired.NodeId, expired.Variant);
        expired.LastFetchedAt = DateTimeOffset.UtcNow.AddHours(-2);
        expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        Nodes().Upsert(expired);
        Assert.Equal(1, Nodes().Count(n => n.FileKey == _fileKey));

        await BuildCache(ttlHours: 1).SetAsync(Slice("2:2"));

        var survivors = Nodes().Find(n => n.FileKey == _fileKey).ToList();
        Assert.Single(survivors);
        Assert.Equal("2:2", survivors[0].NodeId);
    }

    [Fact]
    public async Task LiveSlicesSurviveTheSweep()
    {
        await BuildCache(ttlHours: 1).SetAsync(Slice("3:3"));
        await BuildCache(ttlHours: 1).SetAsync(Slice("4:4"));

        Assert.Equal(2, Nodes().Count(n => n.FileKey == _fileKey));
    }
}
