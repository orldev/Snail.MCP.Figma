using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Application.Documents;

using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Plugin-first READ tools: while a plugin is connected for the file, reads go through it and paid REST is untouched; REST remains the fallback and the forced path via source="rest".</summary>
[Collection(nameof(BridgeHostFixture))]
public class ReadToolsLiveEditorTests
{
    private readonly BridgeHostFixture _bridge;

    public ReadToolsLiveEditorTests(BridgeHostFixture bridge) => _bridge = bridge;

    private static string NewFileKey() => $"rd{Guid.NewGuid().ToString("N")[..10]}";

    private static string Url(string fileKey) => $"https://www.figma.com/design/{fileKey}/Test";

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private (ReadTools Tools, FakePublishedSource Api) Build(bool preferPlugin = true)
    {
        var api = new FakePublishedSource(new JsonObject
        {
            ["name"] = "File from REST",
            ["version"] = "v1",
            ["document"] = new JsonObject
            {
                ["id"] = "0:0",
                ["type"] = "DOCUMENT",
                ["name"] = "File from REST",
                ["children"] = new JsonArray(),
            },
        });

        var config = new ServerConfig
        {
            Cache = { Enabled = false },
            Bridge = { PreferPluginForReads = preferPlugin },
        };

        var database = new DisabledLiteDatabaseProvider();
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

    /// <summary>Sandbox reply to EXECUTE_CODE as the live ui.html returns it: a success/result envelope.</summary>
    private static void AnswerScript(FakeEditorExtension plugin, Func<JsonNode?, JsonNode?> result) =>
        plugin.Handlers[EditorCommands.ExecuteCode.Name] = parameters => new JsonObject
        {
            ["success"] = true,
            ["result"] = result(parameters),
        };

    [Fact]
    public async Task FileTreeComesFromThePluginAndRestIsNeverTouched()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerScript(plugin, _ => new JsonObject
        {
            ["found"] = true,
            ["node"] = new JsonObject
            {
                ["id"] = "0:0",
                ["name"] = "Live file",
                ["type"] = "DOCUMENT",
                ["children"] = new JsonArray(new JsonObject { ["id"] = "1:1", ["name"] = "Page 1", ["type"] = "PAGE" }),
            },
            ["nodeCount"] = 2,
        });

        var (tools, api) = Build();
        var response = Parse(await tools.GetFileDataAsync(Url(fileKey)));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.Equal("Live file", response["data"]!["name"]!.GetValue<string>());
        Assert.Equal(0, api.FileCalls);
    }

    [Fact]
    public async Task ScriptCarriesTheRequestedDepthAndNodeCap()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        JsonNode? seenParams = null;
        AnswerScript(plugin, parameters =>
        {
            seenParams = parameters?.DeepClone();

            return new JsonObject { ["found"] = true, ["node"] = new JsonObject { ["id"] = "0:0" }, ["nodeCount"] = 1 };
        });

        var (tools, _) = Build();
        await tools.GetFileDataAsync(Url(fileKey), depth: 2, maxNodes: 77);

        var code = seenParams?["code"]?.GetValue<string>();
        Assert.NotNull(code);
        Assert.Contains("0, 2, 77", code);
        Assert.Contains("loadAllPagesAsync", code);
    }

    [Fact]
    public async Task ComponentReadGoesThroughThePluginWithAJsonSafeNodeId()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        JsonNode? seenParams = null;
        AnswerScript(plugin, parameters =>
        {
            seenParams = parameters?.DeepClone();

            return new JsonObject
            {
                ["found"] = true,
                ["node"] = new JsonObject { ["id"] = "1:2", ["name"] = "Button", ["type"] = "COMPONENT" },
                ["nodeCount"] = 1,
            };
        });

        var (tools, api) = Build();
        var response = Parse(await tools.GetComponentAsync("1-2", Url(fileKey)));

        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.Equal("Button", response["data"]!["name"]!.GetValue<string>());
        Assert.Equal(0, api.NodeCalls);
        Assert.Contains("\"1:2\"", seenParams?["code"]?.GetValue<string>());
    }

    [Fact]
    public async Task PluginNotFoundIsAuthoritative_NoRestRetry()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerScript(plugin, _ => new JsonObject { ["found"] = false, ["nodeId"] = "9:9" });

        var (tools, api) = Build();
        var response = Parse(await tools.GetComponentAsync("9:9", Url(fileKey)));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("was not found", response["error"]!.GetValue<string>());
        Assert.Equal(0, api.NodeCalls);
    }

    [Fact]
    public async Task VariablesComeFromThePluginOnAnyPlan()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        plugin.Handlers[EditorCommands.GetVariablesData.Name] = _ => new JsonObject
        {
            ["variableCollections"] = new JsonObject
            {
                ["VariableCollectionId:1:1"] = new JsonObject
                {
                    ["name"] = "Colors",
                    ["modes"] = new JsonArray(new JsonObject { ["modeId"] = "1:0", ["name"] = "Light" }),
                    ["variableIds"] = new JsonArray("VariableID:1:2"),
                },
            },
            ["variables"] = new JsonObject
            {
                ["VariableID:1:2"] = new JsonObject
                {
                    ["name"] = "color/bg",
                    ["resolvedType"] = "COLOR",
                    ["variableCollectionId"] = "VariableCollectionId:1:1",
                },
            },
        };

        var (tools, api) = Build();
        var response = Parse(await tools.GetVariablesAsync(Url(fileKey), format: "full"));

        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        var variables = (response["data"]!["variables"] as JsonArray)!;
        Assert.Single(variables);
        Assert.Equal("color/bg", variables[0]!["name"]!.GetValue<string>());
        Assert.Equal(0, api.VariableCalls);
    }

    [Fact]
    public async Task StylesComeFromThePluginInRestShape()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerScript(plugin, _ => new JsonObject
        {
            ["styles"] = new JsonArray(
                new JsonObject { ["key"] = "k1", ["name"] = "Primary", ["style_type"] = "FILL" },
                new JsonObject { ["key"] = "k2", ["name"] = "Body", ["style_type"] = "TEXT" },
                new JsonObject { ["key"] = "k3", ["name"] = "Heading", ["style_type"] = "TEXT" }),
        });

        var (tools, api) = Build();
        var response = Parse(await tools.GetStylesAsync(Url(fileKey)));

        Assert.Equal("plugin", response["source"]!.GetValue<string>());
        Assert.Equal(2, response["data"]!["countByType"]!["TEXT"]!.GetValue<int>());
        Assert.Equal(1, response["data"]!["countByType"]!["FILL"]!.GetValue<int>());
        Assert.Equal(0, api.StyleCalls);
    }

    [Fact]
    public async Task BrokenPluginFallsBackToRestInAutoMode()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        plugin.Errors[EditorCommands.ExecuteCode.Name] = "the sandbox crashed";

        var (tools, api) = Build();
        var response = Parse(await tools.GetFileDataAsync(Url(fileKey)));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.FileCalls);
    }

    [Fact]
    public async Task SourceRestForcesRestEvenWithALivePlugin()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerScript(plugin, _ => new JsonObject { ["found"] = true, ["node"] = new JsonObject { ["id"] = "0:0" } });

        var (tools, api) = Build();
        var response = Parse(await tools.GetFileDataAsync(Url(fileKey), source: "rest"));

        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.FileCalls);
    }

    [Fact]
    public async Task PreferPluginOffKeepsReadsOnRest()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        AnswerScript(plugin, _ => new JsonObject { ["found"] = true, ["node"] = new JsonObject { ["id"] = "0:0" } });

        var (tools, api) = Build(preferPlugin: false);
        var response = Parse(await tools.GetFileDataAsync(Url(fileKey)));

        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.FileCalls);
    }

    [Fact]
    public async Task SourcePluginWithoutASessionExplainsInsteadOfFallingBack()
    {
        var (tools, api) = Build();
        var response = Parse(await tools.GetStylesAsync(Url(NewFileKey()), source: "plugin"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("not connected", response["error"]!.GetValue<string>());
        Assert.Equal(0, api.FileCalls);
        Assert.Equal(0, api.StyleCalls);
    }

    [Fact]
    public async Task SessionInAnotherFileDoesNotHijackTheRead()
    {
        var connected = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);
        AnswerScript(plugin, _ => new JsonObject { ["found"] = true, ["node"] = new JsonObject { ["id"] = "0:0" } });

        var (tools, api) = Build();
        var response = Parse(await tools.GetFileDataAsync(Url(NewFileKey())));

        Assert.Equal("figma-api", response["source"]!.GetValue<string>());
        Assert.Equal(1, api.FileCalls);
    }
}
