using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Application.Documents;

/// <summary>Stage 1 definition of done: repeated reads do not hit the Figma API and a version change drops the cache; uses a real LiteDB in a temp directory.</summary>
[Collection(nameof(LiteDbFixture))]
public class DocumentReaderTests(LiteDbFixture cacheFixture) : IAsyncLifetime
{
    private readonly LiteDbFixture _cacheFixture = cacheFixture;
    private readonly string _fileKey = $"file-{Guid.NewGuid().ToString("N")[..8]}";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    private static JsonNode SampleFile() => new JsonObject
    {
        ["name"] = "Test file",
        ["document"] = new JsonObject
        {
            ["id"] = "0:0",
            ["name"] = "Document",
            ["type"] = "DOCUMENT",
            ["children"] = new JsonArray(new JsonObject
            {
                ["id"] = "0:1",
                ["name"] = "Page 1",
                ["type"] = "CANVAS",
                ["absoluteBoundingBox"] = new JsonObject { ["x"] = 0, ["y"] = 0 },
            }),
        },
    };

    private (DocumentReader Reader, FakePublishedSource Api, LiteDbNodeSnapshotStore Cache) Build(
        int ttlHours = 1,
        int freshnessSeconds = 300)
    {
        var api = new FakePublishedSource(SampleFile());
        var cacheOptions = new CacheOptions
        {
            Enabled = true,
            TtlHours = ttlHours,
            FreshnessSeconds = freshnessSeconds,
        };
        var cache = new LiteDbNodeSnapshotStore(_cacheFixture.Database, cacheOptions, NullLogger<LiteDbNodeSnapshotStore>.Instance);
        var images = new LiteDbRenderSnapshotStore(_cacheFixture.Database, cacheOptions, NullLogger<LiteDbRenderSnapshotStore>.Instance);
        var validator = new LiteDbDocumentVersionLedger(_cacheFixture.Database, cache, images, NullLogger<LiteDbDocumentVersionLedger>.Instance);
        var sanitizer = new NodeSanitizer(new SanitizationOptions { LogMetrics = false }, NullLogger<NodeSanitizer>.Instance);

        return (
            new DocumentReader(api, cache, validator, sanitizer, cacheOptions, NullLogger<DocumentReader>.Instance),
            api,
            cache);
    }

    [Fact]
    public async Task SecondReadComesFromCacheAndSkipsTheApi()
    {
        var (reader, api, _) = Build();
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive, MaxDepth = 3 };

        var first = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions { Depth = 3 }, request);
        Assert.False(first.IsFromCache);
        Assert.Equal(1, api.FileCalls);

        var second = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions { Depth = 3 }, request);
        Assert.True(second.IsFromCache);
        Assert.Equal(1, api.FileCalls);
        Assert.True(JsonNode.DeepEquals(first.Node, second.Node));
    }

    [Fact]
    public async Task DifferentProfileIsADifferentSliceAndNotServedFromTheSameEntry()
    {
        var (reader, api, _) = Build();

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), new SanitizationRequest { Profile = SanitizationProfile.Aggressive });
        var balanced = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), new SanitizationRequest { Profile = SanitizationProfile.Balanced });

        Assert.False(balanced.IsFromCache);
        Assert.Equal(2, api.FileCalls);
        Assert.Contains("absoluteBoundingBox", balanced.Node.ToJsonString());
    }

    [Fact]
    public async Task ChangedFileVersionInvalidatesTheCache()
    {
        var (reader, api, _) = Build(freshnessSeconds: 0);
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.Equal(1, api.FileCalls);

        api.Version = "v2";

        var afterEdit = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.False(afterEdit.IsFromCache);
        Assert.Equal(2, api.FileCalls);
    }

    [Fact]
    public async Task InvalidateFileRemovesEveryStoredSlice()
    {
        var (reader, api, cache) = Build();
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        var removed = await cache.InvalidateFileAsync(_fileKey);
        Assert.True(removed >= 1);

        var afterInvalidation = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.False(afterInvalidation.IsFromCache);
        Assert.Equal(2, api.FileCalls);
    }

    [Fact]
    public async Task ExpiredSliceIsNotServedFromCache()
    {
        var (reader, api, _) = Build(ttlHours: -1);
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        var second = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);

        Assert.False(second.IsFromCache);
        Assert.Equal(2, api.FileCalls);
    }

    [Fact]
    public async Task InvalidateNodesAlsoDropsTheWholeFileSlice()
    {
        var (reader, api, cache) = Build();
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.Equal(1, api.FileCalls);

        await cache.InvalidateNodesAsync(_fileKey, ["0:1"]);

        var afterMutation = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.False(afterMutation.IsFromCache);
        Assert.Equal(2, api.FileCalls);
    }

    [Fact]
    public async Task OneReadCostsOneApiCall()
    {
        var (reader, api, _) = Build(freshnessSeconds: 0);
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);

        Assert.Equal(1, api.FileCalls);
        Assert.Equal(0, api.NodeCalls);
    }

    [Fact]
    public async Task FreshSliceIsServedWithoutTouchingFigmaAtAll()
    {
        var (reader, api, _) = Build(freshnessSeconds: 300);
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);

        for (var i = 0; i < 5; i++)
        {
            var repeat = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
            Assert.True(repeat.IsFromCache);
            Assert.False(repeat.IsStale);
        }

        Assert.Equal(1, api.FileCalls);
    }

    [Fact]
    public async Task UnavailableFigmaServesTheStaleSliceInsteadOfFailing()
    {
        var (reader, api, _) = Build(freshnessSeconds: 0);
        var request = new SanitizationRequest { Profile = SanitizationProfile.Aggressive };

        var first = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);
        Assert.False(first.IsFromCache);

        api.ShouldFailFileRequests = true;

        var underRateLimit = await reader.ReadFileAsync(_fileKey, new DocumentReadOptions(), request);

        Assert.True(underRateLimit.IsFromCache);
        Assert.True(underRateLimit.IsStale);
        Assert.True(JsonNode.DeepEquals(first.Node, underRateLimit.Node));
    }

    [Fact]
    public async Task UnavailableFigmaWithNothingCachedStillReportsTheError()
    {
        var (reader, api, _) = Build(freshnessSeconds: 0);
        api.ShouldFailFileRequests = true;

        await Assert.ThrowsAsync<PublishedSourceException>(() => reader.ReadFileAsync(
            _fileKey, new DocumentReadOptions(), new SanitizationRequest { Profile = SanitizationProfile.Aggressive }));
    }

    [Fact]
    public async Task VersionIsTakenFromThePayloadNotFromASeparateRequest()
    {
        var (reader, api, _) = Build(freshnessSeconds: 0);

        var read = await reader.ReadFileAsync(
            _fileKey, new DocumentReadOptions(), new SanitizationRequest { Profile = SanitizationProfile.Aggressive });

        Assert.Equal("v1", read.FileVersion);
        Assert.Equal(1, api.FileCalls);
    }
}
