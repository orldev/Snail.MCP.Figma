using ModelContextProtocol.Protocol;
using System.Text;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Application.Documents;

using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Source selection of figma_get_component_image: REST is the only path that costs quota, so cache or plugin must win while they can; the cache itself is covered by <see cref="DocumentRendererTests"/>.</summary>
[Collection(nameof(BridgeHostFixture))]
public class ReadToolsRenderTests : IDisposable
{
    private readonly BridgeHostFixture _bridge;
    private readonly string _screenshotDirectory =
        Path.Combine(Path.GetTempPath(), "snail-image-tests", Guid.NewGuid().ToString("N"));

    public ReadToolsRenderTests(BridgeHostFixture bridge) => _bridge = bridge;

    private static string NewFileKey() => $"img{Guid.NewGuid().ToString("N")[..10]}";

    /// <summary>The tool takes a link, not a bare key, and extracts the key itself.</summary>
    private static string Url(string fileKey) => $"https://www.figma.com/design/{fileKey}/Test";

    /// <summary>One-pixel PNG — exactly how the plugin returns an image, base64 inside image.</summary>
    private static readonly string SamplePng = Convert.ToBase64String(Encoding.UTF8.GetBytes("PNG-DATA"));

    private (ReadTools Tools, FakePublishedSource Api) Build(bool preferPlugin = true, ILiteDatabaseProvider? database = null)
    {
        var api = new FakePublishedSource(new JsonObject());
        var config = new ServerConfig
        {
            DataDirectory = _screenshotDirectory,
            Cache = { Enabled = database is not null },
            Bridge = { PreferPluginForReads = preferPlugin },
        };
        database ??= new DisabledLiteDatabaseProvider();
        var nodes = new LiteDbNodeSnapshotStore(database, config.Cache, NullLogger<LiteDbNodeSnapshotStore>.Instance);
        var images = new LiteDbRenderSnapshotStore(database, config.Cache, NullLogger<LiteDbRenderSnapshotStore>.Instance);
        var versions = new LiteDbDocumentVersionLedger(database, nodes, images, NullLogger<LiteDbDocumentVersionLedger>.Instance);
        var sanitization = new SanitizationOptions { LogMetrics = false };
        var sanitizer = new NodeSanitizer(sanitization, NullLogger<NodeSanitizer>.Instance);

        var tools = new ReadTools(
            api,
            new DocumentReader(api, nodes, versions, sanitizer, config.Cache, NullLogger<DocumentReader>.Instance),
            new DocumentRenderer(api, images, versions, NullLogger<DocumentRenderer>.Instance),
            _bridge.Gateway,
            new CaptureStore(config, NullLogger<CaptureStore>.Instance),
            new ActiveDocumentTracker(),
            sanitizer,
            sanitization,
            config.Bridge,
            NullLogger<ReadTools>.Instance);

        return (tools, api);
    }

    private static void AnswerWithScreenshot(FakeEditorExtension plugin) =>
        plugin.Handlers[EditorCommands.CaptureScreenshot.Name] = _ => new JsonObject
        {
            ["image"] = new JsonObject
            {
                ["base64"] = SamplePng,
                ["format"] = "PNG",
                ["scale"] = 2,
            },
        };

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    /// <summary>Text of a result that may also carry a picture; the JSON block is always the first one.</summary>
    private static JsonNode Parse(CallToolResult result) =>
        Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text);

    [Fact]
    public async Task PluginCaptureComesBackAsContentWhenAskedForBoth()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        var (tools, _) = Build();
        var result = await tools.GetComponentImageAsync("1:2", Url(fileKey), deliver: "both");

        Assert.Equal(2, result.Content.Count);

        var picture = Assert.IsType<ImageContentBlock>(result.Content[1]);

        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal(SamplePng, Encoding.UTF8.GetString(picture.Data.Span));
        Assert.True(File.Exists(Parse(result)["data"]!["image"]!["path"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ConnectedPluginRendersTheNodeWithoutSpendingQuota()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        var (tools, api) = Build();
        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(fileKey)));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.True(File.Exists(response["data"]!["image"]!["path"]!.GetValue<string>()));
        Assert.Equal(0, api.ImageCalls);
    }

    [Fact]
    public async Task WithoutAPluginSessionForThisFileTheRenderGoesToRest()
    {
        var connected = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);
        AnswerWithScreenshot(plugin);

        var (tools, api) = Build();
        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(NewFileKey())));

        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Contains("https://figma.test/", response["data"]!["url"]!.GetValue<string>());
        Assert.Equal(1, api.ImageCalls);
    }

    /// <summary>Live cache file in a temp directory — for plugin-versus-cache priority tests.</summary>
    private LiteDatabaseProvider WarmDatabase() => new(
        new ServerConfig { DataDirectory = Path.Combine(_screenshotDirectory, $"db-{Guid.NewGuid().ToString("N")}") },
        NullLogger<LiteDatabaseProvider>.Instance);

    private static async Task SeedCachedRenderAsync(ILiteDatabaseProvider database, string fileKey, string nodeId)
    {
        var images = new LiteDbRenderSnapshotStore(database, new ServerConfig().Cache, NullLogger<LiteDbRenderSnapshotStore>.Instance);
        await images.SetAsync(new RenderSnapshot
        {
            FileKey = fileKey,
            NodeId = nodeId,
            Variant = DocumentRenderer.BuildVariant("png", 2),
            Url = "https://figma.test/cached",
        });
    }

    [Fact]
    public async Task LivePluginBeatsAWarmCache_FreshnessCostsNothing()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        using var database = WarmDatabase();
        await SeedCachedRenderAsync(database, fileKey, "1:2");

        var (tools, api) = Build(database: database);
        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(fileKey)));

        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.Equal(0, api.ImageCalls);
    }

    [Fact]
    public async Task PreferPluginOffRestoresCacheFirstOrder()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        using var database = WarmDatabase();
        await SeedCachedRenderAsync(database, fileKey, "1:2");

        var (tools, api) = Build(preferPlugin: false, database: database);
        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(fileKey)));

        Assert.Equal("cache", response["source"]!.GetValue<string>());
        Assert.Equal("https://figma.test/cached", response["data"]!["url"]!.GetValue<string>());
        Assert.Equal(0, api.ImageCalls);
    }

    [Fact]
    public async Task ExhaustedQuotaFallsBackToThePluginInsteadOfFailing()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        var (tools, api) = Build();
        api.FailImageRequests = true;

        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(fileKey), source: "rest"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.ImageCalls);
    }

    [Fact]
    public async Task ExhaustedQuotaWithoutAPluginExplainsHowToGetThePictureAnyway()
    {
        var (tools, api) = Build();
        api.FailImageRequests = true;

        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(NewFileKey())));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("429", response["error"]!.GetValue<string>());
        Assert.Contains("Figma Desktop", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task PluginOnlyModeDoesNotSilentlyFallBackToRest()
    {
        var (tools, api) = Build();

        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(NewFileKey()), source: "plugin"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal(0, api.ImageCalls);
        Assert.Contains("rest", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task VectorFormatsStayOnRestEvenWithAPluginConnected()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerWithScreenshot(plugin);

        var (tools, api) = Build();
        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(fileKey), format: "svg"));

        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.ImageCalls);
    }

    [Fact]
    public async Task UnknownSourceIsRejectedBeforeAnyRequest()
    {
        var (tools, api) = Build();

        var response = Parse(await tools.GetComponentImageAsync("1:2", Url(NewFileKey()), source: "magic"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal(0, api.ImageCalls);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_screenshotDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
