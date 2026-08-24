using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Ports;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Plugin-API tools built on EXECUTE_CODE: parameters must land in the script as data, never as code.</summary>
[Collection(nameof(BridgeHostFixture))]
public class PluginScriptToolsTests
{
    private readonly BridgeHostFixture _bridge;

    public PluginScriptToolsTests(BridgeHostFixture bridge) => _bridge = bridge;

    private static string NewFileKey() => $"ps{Guid.NewGuid().ToString("N")[..10]}";

    private WriteTools Write() => new(_bridge.Gateway, NullLogger<WriteTools>.Instance);

    private NodeTools Nodes() => new(_bridge.Gateway, NullLogger<NodeTools>.Instance);

    private VersionTools Versions()
    {
        var api = new FakePublishedSource(new JsonObject());

        return new VersionTools(
            _bridge.Gateway, api, api,
            new Snail.MCP.Figma.Application.Documents.ActiveDocumentTracker(),
            new Snail.MCP.Figma.Application.Diff.VersionSnapshotCache(4),
            NullLogger<VersionTools>.Instance);
    }

    private SessionTools Session() => new(
        _bridge.Gateway,
        new Snail.MCP.Figma.Application.Documents.ActiveDocumentTracker(),
        new CaptureSink(),
        NullLogger<SessionTools>.Instance);

    /// <summary>Real capture behavior in a temp folder, so binary exports land on disk like in production.</summary>
    private sealed class CaptureSink : ICaptureStore
    {
        public JsonObject SaveAndReplace(JsonObject image)
        {
            var bytes = Convert.FromBase64String(image["base64"]!.GetValue<string>());
            var path = Path.Combine(Path.GetTempPath(), "snail-export-tests", $"{Guid.NewGuid().ToString("N")}.{(image["format"]?.GetValue<string>() ?? "bin").ToLowerInvariant()}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            image.Remove("base64");
            image["path"] = path;

            return image;
        }
    }

    private QualityTools Quality() => new(_bridge.Gateway, NullLogger<QualityTools>.Instance);

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    /// <summary>Sandbox reply to EXECUTE_CODE exactly as the live ui.html shapes it; captures the sent script.</summary>
    private static void AnswerScript(FakeEditorExtension plugin, JsonNode? result, Action<string>? onCode = null) =>
        plugin.Handlers[EditorCommands.ExecuteCode.Name] = parameters =>
        {
            onCode?.Invoke(parameters?["code"]?.GetValue<string>() ?? string.Empty);

            return new JsonObject { ["success"] = true, ["result"] = result?.DeepClone() };
        };

    [Fact]
    public async Task CommitUndoSendsTheCommitAndUnwrapsTheReply()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["committed"] = true }, c => code = c);

        var response = Parse(await Write().CommitUndoAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.True(response["data"]!["committed"]!.GetValue<bool>());
        Assert.Contains("figma.commitUndo()", code);
    }

    [Fact]
    public async Task CreateStyleRejectsAnUnknownTypeWithoutTouchingThePlugin()
    {
        var response = Parse(await Write().CreateStyleAsync("SHADOW", "Elevation/1"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("PAINT, TEXT, EFFECT or GRID", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateStyleEmbedsNameAndPropertiesAsData()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin,
            new JsonObject { ["id"] = "S:1", ["name"] = "Brand/Primary", ["styleType"] = "PAINT" },
            c => code = c);

        var response = Parse(await Write().CreateStyleAsync(
            "paint", "Brand/Primary", """{"paints":[{"type":"SOLID","color":{"r":1,"g":0,"b":0}}]}"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("S:1", response["data"]!["id"]!.GetValue<string>());
        Assert.Contains("figma.createPaintStyle()", code);
        Assert.Contains("\"Brand/Primary\"", code);
        Assert.Contains("\"paints\"", code);
    }

    [Fact]
    public async Task CreateStyleSurfacesTheSandboxError()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        AnswerScript(plugin, new JsonObject { ["__error"] = "Cannot write to node" });

        var response = Parse(await Write().CreateStyleAsync("TEXT", "Body"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Cannot write to node", response["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreatePageCarriesTheNameAndTheCurrentPageSwitch()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["id"] = "7:0", ["name"] = "Icons" }, c => code = c);

        var response = Parse(await Nodes().CreatePageAsync("Icons", makeCurrent: true));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("7:0", response["data"]!["id"]!.GetValue<string>());
        Assert.Contains("figma.createPage()", code);
        Assert.Contains("\"Icons\"", code);
        Assert.Contains("setCurrentPageAsync", code);
    }

    [Fact]
    public async Task CreateNodeFromSvgRejectsEmptyMarkup()
    {
        var response = Parse(await Nodes().CreateNodeFromSvgAsync("  "));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CreateNodeFromSvgNormalizesTheParentAndEmbedsTheMarkup()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin,
            new JsonObject { ["id"] = "9:1", ["name"] = "icon", ["width"] = 24, ["height"] = 24, ["parentId"] = "1:2" },
            c => code = c);

        var response = Parse(await Nodes().CreateNodeFromSvgAsync(
            "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>", parentId: "1-2", name: "icon", x: 10, y: 20));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("9:1", response["data"]!["id"]!.GetValue<string>());
        Assert.Contains("\"1:2\"", code);
        Assert.Contains("figma.createNodeFromSvg", code);
        Assert.Contains("viewBox", code);
    }

    [Fact]
    public async Task AddAnnotationCategoryValidatesTheColorLocally()
    {
        var response = Parse(await Quality().AddAnnotationCategoryAsync("Handoff", "purple"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("yellow", response["hint"]!.GetValue<string>());
    }

    [Fact]
    public async Task AddAnnotationCategorySendsLabelAndColorAsData()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin,
            new JsonObject { ["id"] = "c1", ["label"] = "Handoff", ["color"] = "teal", ["isPreset"] = false },
            c => code = c);

        var response = Parse(await Quality().AddAnnotationCategoryAsync("Handoff", "Teal"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("teal", response["data"]!["color"]!.GetValue<string>());
        Assert.Contains("addAnnotationCategoryAsync", code);
        Assert.Contains("\"Handoff\"", code);
        Assert.Contains("\"teal\"", code);
    }

    [Fact]
    public async Task ExportNodeReturnsSvgMarkupDirectly()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["format"] = "SVG", ["svg"] = "<svg/>", ["length"] = 6 }, c => code = c);

        var response = Parse(await Session().ExportNodeAsync("1-2", "svg"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("<svg/>", response["data"]!["svg"]!.GetValue<string>());
        Assert.Contains("SVG_STRING", code);
        Assert.Contains("\"1:2\"", code);
    }

    [Fact]
    public async Task ExportNodeSavesBinaryFormatsToDisk()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        var pixel = Convert.ToBase64String([137, 80, 78, 71]);
        AnswerScript(plugin, new JsonObject { ["format"] = "PNG", ["base64"] = pixel, ["byteLength"] = 4 });

        var response = Parse(await Session().ExportNodeAsync("1:2", "PNG", scale: 2));

        Assert.True(response["ok"]!.GetValue<bool>());
        var path = response["data"]!["path"]!.GetValue<string>();
        Assert.True(File.Exists(path), path);
        Assert.Null(response["data"]!["base64"]);
    }

    [Fact]
    public async Task ExportNodeRejectsAnUnknownFormat()
    {
        var response = Parse(await Session().ExportNodeAsync("1:2", "BMP"));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ActiveUsersComeBackSplitIntoMeAndOthers()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        AnswerScript(plugin, new JsonObject
        {
            ["me"] = new JsonObject { ["name"] = "orldev", ["sessionId"] = 1 },
            ["others"] = new JsonArray(new JsonObject { ["name"] = "Ann", ["sessionId"] = 2 }),
            ["totalInFile"] = 2,
        });

        var response = Parse(await Session().GetActiveUsersAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("orldev", response["data"]!["me"]!["name"]!.GetValue<string>());
        Assert.Equal(2, response["data"]!["totalInFile"]!.GetValue<int>());
    }

    [Fact]
    public async Task PlaceImageDemandsExactlyOneSource()
    {
        var both = Parse(await Nodes().PlaceImageAsync(url: "https://x/y.png", base64: "AA=="));
        var neither = Parse(await Nodes().PlaceImageAsync());

        Assert.False(both["ok"]!.GetValue<bool>());
        Assert.False(neither["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PlaceImageBuildsTheRectangleWithAnImageFill()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin,
            new JsonObject { ["id"] = "5:1", ["width"] = 200, ["height"] = 100, ["imageHash"] = "h1", ["parentId"] = "0:1" },
            c => code = c);

        var response = Parse(await Nodes().PlaceImageAsync(url: "https://example.com/pic.png", name: "pic"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("5:1", response["data"]!["id"]!.GetValue<string>());
        Assert.Contains("createImageAsync", code);
        Assert.Contains("IMAGE", code);
    }

    [Fact]
    public async Task JsxSpecIsEmbeddedAndBuiltThroughTheWidgetFactory()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin,
            new JsonObject { ["id"] = "6:1", ["name"] = "card", ["type"] = "FRAME", ["width"] = 240, ["height"] = 80 },
            c => code = c);

        var response = Parse(await Nodes().CreateNodeFromJsxAsync(
            """{"type":"AutoLayout","props":{"fill":"#F00"},"children":[{"type":"Text","children":["Hi"]}]}"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("6:1", response["data"]!["id"]!.GetValue<string>());
        Assert.Contains("createNodeFromJSXAsync", code);
        Assert.Contains("AutoLayout", code);
        Assert.Contains("figma.widget", code);
    }

    [Fact]
    public async Task JsxRejectsMalformedSpecLocally()
    {
        var response = Parse(await Nodes().CreateNodeFromJsxAsync("{not json"));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SaveVersionEmbedsTitleAndDescription()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["versionId"] = "123", ["title"] = "Sprint 4" }, c => code = c);

        var response = Parse(await Versions().SaveVersionAsync("Sprint 4", "Buttons reworked"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("123", response["data"]!["versionId"]!.GetValue<string>());
        Assert.Contains("saveVersionHistoryAsync", code);
        Assert.Contains("Buttons reworked", code);
    }

    [Fact]
    public async Task SaveVersionRejectsAnEmptyTitle()
    {
        var response = Parse(await Versions().SaveVersionAsync("  "));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task NotifyPassesTimeoutAndErrorFlag()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["shown"] = true }, c => code = c);

        var response = Parse(await Session().NotifyAsync("Done!", timeoutMs: 6000, isError: true));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Contains("figma.notify", code);
        Assert.Contains("6000", code);
        Assert.Contains("error: true", code);
    }

    [Fact]
    public async Task ScrollToNodeSwitchesPageAndCanSelect()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["centered"] = "1:2", ["page"] = "Icons", ["zoom"] = 1.5 }, c => code = c);

        var response = Parse(await Session().ScrollToNodeAsync("1-2", select: true, zoom: 1.5));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Contains("scrollAndZoomIntoView", code);
        Assert.Contains("setCurrentPageAsync", code);
        Assert.Contains("\"1:2\"", code);
    }

    [Fact]
    public async Task FileThumbnailClearsWhenNoNodeGiven()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["thumbnail"] = null }, c => code = c);

        var response = Parse(await Session().SetFileThumbnailAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Contains("setFileThumbnailNodeAsync(null)", code);
    }

    [Fact]
    public async Task BooleanOperationValidatesOperationAndArity()
    {
        var unknown = Parse(await Nodes().BooleanOperationAsync("MERGE", "1:1,1:2"));
        var tooFew = Parse(await Nodes().BooleanOperationAsync("UNION", "1:1"));

        Assert.False(unknown["ok"]!.GetValue<bool>());
        Assert.False(tooFew["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task BooleanOperationSendsNormalizedIdsInOrder()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["id"] = "9:9", ["type"] = "BOOLEAN_OPERATION", ["name"] = "icon" }, c => code = c);

        var response = Parse(await Nodes().BooleanOperationAsync("subtract", "1-1, 1-2", name: "icon"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Contains("figma.subtract", code);
        Assert.Contains("[\"1:1\",\"1:2\"]", code);
    }

    [Fact]
    public async Task GroupAndUngroupRoundTripThroughTheSandbox()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["id"] = "7:7", ["name"] = "grp", ["children"] = 2 }, c => code = c);

        var grouped = Parse(await Nodes().GroupNodesAsync("1:1,1:2", "grp"));
        Assert.True(grouped["ok"]!.GetValue<bool>());
        Assert.Contains("figma.group", code);

        AnswerScript(plugin, new JsonObject
        {
            ["released"] = new JsonArray(new JsonObject { ["id"] = "1:1", ["name"] = "a" }),
        }, c => code = c);

        var ungrouped = Parse(await Nodes().UngroupNodeAsync("7:7"));
        Assert.True(ungrouped["ok"]!.GetValue<bool>());
        Assert.Contains("figma.ungroup", code);
    }

    [Fact]
    public async Task SelectionColorsComeBackCompact()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        AnswerScript(plugin, new JsonObject
        {
            ["paints"] = new JsonArray(new JsonObject { ["type"] = "SOLID" }),
            ["styles"] = new JsonArray(new JsonObject { ["id"] = "S:1", ["name"] = "Brand/Primary" }),
        });

        var response = Parse(await Write().GetSelectionColorsAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("Brand/Primary", response["data"]!["styles"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SortStylesDefaultsToAlphabeticalAndAcceptsExplicitOrder()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        _bridge.Registry.SetActiveFile(fileKey);
        string? code = null;
        AnswerScript(plugin, new JsonObject { ["sorted"] = 3, ["first"] = "A", ["last"] = "C" }, c => code = c);

        var alphabetical = Parse(await Write().SortStylesAsync("paint"));
        Assert.True(alphabetical["ok"]!.GetValue<bool>());
        Assert.Contains("localeCompare", code);
        Assert.Contains("moveLocalPaintStyleAfter", code);

        var explicitOrder = Parse(await Write().SortStylesAsync("TEXT", "S:2,S:1"));
        Assert.True(explicitOrder["ok"]!.GetValue<bool>());
        Assert.Contains("moveLocalTextStyleAfter", code);
        Assert.Contains("S:2", code);

        var bad = Parse(await Write().SortStylesAsync("SHADOW"));
        Assert.False(bad["ok"]!.GetValue<bool>());
    }
}
