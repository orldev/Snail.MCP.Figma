using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Application.Documents;

/// <summary>Render cache: a repeated "show the same node" must not spend the most expensive bucket, and the cache must not survive a node edit.</summary>
[Collection(nameof(SnapshotDatabaseFixture))]
public class DocumentRendererTests(SnapshotDatabaseFixture cacheFixture)
{
    private readonly SnapshotDatabaseFixture _cacheFixture = cacheFixture;

    private static string NewFileKey() => $"img{Guid.NewGuid().ToString("N")[..10]}";

    private (DocumentRenderer Renderer, FakePublishedSource Api, SqliteRenderSnapshotStore Cache, SqliteDocumentVersionLedger Versions) Build()
    {
        var (renderer, api, images, versions, _) = BuildWithListener();

        return (renderer, api, images, versions);
    }

    private (DocumentRenderer Renderer, FakePublishedSource Api, SqliteRenderSnapshotStore Cache, SqliteDocumentVersionLedger Versions, SnapshotInvalidator Listener) BuildWithListener()
    {
        var options = new CacheOptions { Enabled = true, TtlHours = 1, ImageTtlHours = 1 };
        var api = new FakePublishedSource(new System.Text.Json.Nodes.JsonObject());
        var nodes = new SqliteNodeSnapshotStore(_cacheFixture.Database, options, NullLogger<SqliteNodeSnapshotStore>.Instance);
        var images = new SqliteRenderSnapshotStore(_cacheFixture.Database, options, NullLogger<SqliteRenderSnapshotStore>.Instance);
        var versions = new SqliteDocumentVersionLedger(_cacheFixture.Database, nodes, images, NullLogger<SqliteDocumentVersionLedger>.Instance);
        var listener = new SnapshotInvalidator(nodes, images, versions, NullLogger<SnapshotInvalidator>.Instance);

        return (
            new DocumentRenderer(api, images, versions, NullLogger<DocumentRenderer>.Instance),
            api,
            images,
            versions,
            listener);
    }

    [Fact]
    public async Task DocumentRenderer_SameNodeRenderedAgain_CostsNoApiCall()
    {
        var (renderer, api, _, _) = Build();
        var fileKey = NewFileKey();

        var first = await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);
        var second = await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);

        Assert.Equal(1, api.ImageCalls);
        Assert.Equal(first.Urls["1:2"], second.Urls["1:2"]);
        Assert.True(second.IsFullyFromCache);
        Assert.Contains("1:2", second.FromCache);
    }

    [Fact]
    public async Task DocumentRenderer_OtherScaleOrFormat_GoesToFigma()
    {
        var (renderer, api, _, _) = Build();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);
        await renderer.RenderAsync(fileKey, ["1:2"], "png", 1);
        await renderer.RenderAsync(fileKey, ["1:2"], "jpg", 2);

        Assert.Equal(3, api.ImageCalls);
    }

    [Fact]
    public async Task DocumentRenderer_BatchPartlyCached_FetchesOnlyTheMisses()
    {
        var (renderer, api, _, _) = Build();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2", "1:3"], "png", 2);
        var second = await renderer.RenderAsync(fileKey, ["1:2", "1:3", "1:4"], "png", 2);

        Assert.Equal(2, api.ImageCalls);
        Assert.Equal(["1:4"], api.ImageRequests[1]);
        Assert.Equal(3, second.Urls.Count);
        Assert.Equal(["1:4"], second.Fetched);
    }

    [Fact]
    public async Task DocumentRenderer_DashedNodeIdFromUrl_HitsTheSameCacheEntry()
    {
        var (renderer, api, _, _) = Build();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);
        var cached = await renderer.TryGetCachedAsync(fileKey, "1-2", "png", 2);

        Assert.NotNull(cached);
        Assert.Equal(1, api.ImageCalls);
    }

    [Fact]
    public async Task DocumentRenderer_NodeChanged_DropsItsPicture()
    {
        var (renderer, api, cache, _) = Build();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2", "1:3"], "png", 2);
        await cache.InvalidateNodesAsync(fileKey, ["1:2"]);

        var second = await renderer.RenderAsync(fileKey, ["1:2", "1:3"], "png", 2);

        Assert.Equal(2, api.ImageCalls);
        Assert.Equal(["1:2"], api.ImageRequests[1]);
        Assert.Equal(["1:3"], second.FromCache);
    }

    [Fact]
    public async Task DocumentRenderer_NewFileVersion_InvalidatesRenders()
    {
        var (renderer, api, _, versions) = Build();
        var fileKey = NewFileKey();

        await versions.RecordAsync(fileKey, new DocumentVersion("v1", null, null));
        await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);

        await versions.RecordAsync(fileKey, new DocumentVersion("v2", null, null));

        await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);

        Assert.Equal(2, api.ImageCalls);
    }

    [Fact]
    public async Task DocumentRenderer_DocumentChanged_DropsAllPicturesOfTheFile()
    {
        var (renderer, api, _, _, listener) = BuildWithListener();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2", "1:99"], "png", 2);

        await listener.OnDocumentChangedAsync(fileKey, ["1:2"], hasStyleChanges: false);

        var second = await renderer.RenderAsync(fileKey, ["1:2", "1:99"], "png", 2);

        Assert.Equal(2, api.ImageCalls);
        Assert.Empty(second.FromCache);
        Assert.Equal(2, second.Fetched.Count);
    }

    [Fact]
    public async Task DocumentRenderer_AnotherFileChanged_KeepsThisFilesPictures()
    {
        var (renderer, api, _, _, listener) = BuildWithListener();
        var fileKey = NewFileKey();

        await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);

        await listener.OnDocumentChangedAsync(NewFileKey(), ["1:2"], hasStyleChanges: false);

        var second = await renderer.RenderAsync(fileKey, ["1:2"], "png", 2);

        Assert.Equal(1, api.ImageCalls);
        Assert.True(second.IsFullyFromCache);
    }

    [Fact]
    public async Task DocumentRenderer_CacheLookupMisses_NeverGoesToFigma()
    {
        var (renderer, api, _, _) = Build();

        var cached = await renderer.TryGetCachedAsync(NewFileKey(), "1:2", "png", 2);

        Assert.Null(cached);
        Assert.Equal(0, api.ImageCalls);
    }
}
