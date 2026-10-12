using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
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

    private NodeTools Nodes(FakeImageFiles? images = null) =>
        new(_bridge.Gateway, images ?? new FakeImageFiles(), NullLogger<NodeTools>.Instance);

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

    /// <summary>Text of a result that may also carry a picture; the JSON block is always the first one.</summary>
    private static JsonNode Parse(CallToolResult result) =>
        Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text);

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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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

    private static (FakeEditorExtension Plugin, List<string> Codes) Recording(FakeEditorExtension plugin, int failingStep = 0)
    {
        var codes = new List<string>();

        plugin.Handlers[EditorCommands.ExecuteCode.Name] = parameters =>
        {
            codes.Add(parameters?["code"]?.GetValue<string>() ?? string.Empty);

            return codes.Count == failingStep
                ? new JsonObject { ["success"] = false, ["error"] = "TypeError: parent is undefined" }
                : new JsonObject { ["success"] = true, ["result"] = new JsonObject { ["id"] = $"1:{codes.Count}" } };
        };

        return (plugin, codes);
    }

    /// <summary>The point of the tool: one script per step, so the canvas fills in element by element instead of at once.</summary>
    [Fact]
    public async Task StepsRunOneAfterAnotherInTheOrderGiven()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        var (_, codes) = Recording(plugin);

        var response = Parse(await Write().ExecuteStepsAsync("""["first();", "second();", "third();"]"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(3, response["data"]!["completed"]!.GetValue<int>());
        Assert.Equal(["first();", "second();", "third();"], codes);
        Assert.Equal("1:2", response["data"]!["steps"]![1]!["result"]!["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFailingStepIsNamedAndTheRestIsNotRun()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        var (_, codes) = Recording(plugin, failingStep: 2);

        var response = Parse(await Write().ExecuteStepsAsync("""["first();", "second();", "third();"]"""));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Step 2 of 3", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("parent is undefined", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, response["data"]!["completed"]!.GetValue<int>());
        Assert.Equal(2, codes.Count);
    }

    /// <summary>A step is arbitrary code, so its result is whatever it returned: a number or a string, not only an object.</summary>
    /// <remarks>The loop read every reply as an object to look for the failure marker, so a step ending in <c>return 42</c> failed the whole call with "The node must be of type 'JsonObject'".</remarks>
    [Fact]
    public async Task AStepThatReturnsAPlainValueIsCarriedThrough()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        var answers = new Queue<JsonNode?>([JsonValue.Create(42), JsonValue.Create("two")]);

        plugin.Handlers[EditorCommands.ExecuteCode.Name] = _ =>
            new JsonObject { ["success"] = true, ["result"] = answers.Dequeue() };

        var response = Parse(await Write().ExecuteStepsAsync("""["return 42;", "return 'two';"]"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(2, response["data"]!["completed"]!.GetValue<int>());
        Assert.Equal(42, response["data"]!["steps"]![0]!["result"]!.GetValue<int>());
        Assert.Equal("two", response["data"]!["steps"]![1]!["result"]!.GetValue<string>());
    }

    [Fact]
    public async Task WithoutStopOnErrorTheRemainingStepsStillRun()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        var (_, codes) = Recording(plugin, failingStep: 2);

        var response = Parse(await Write().ExecuteStepsAsync("""["a();", "b();", "c();"]""", stopOnError: false));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal(2, response["data"]!["completed"]!.GetValue<int>());
        Assert.Equal(3, codes.Count);
    }

    /// <summary>The pause is what makes the build watchable, so it has to reach the loop rather than only the description.</summary>
    [Fact]
    public async Task DelayPacesTheBuildBetweenSteps()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        Recording(plugin);

        var started = Stopwatch.StartNew();
        var response = Parse(await Write().ExecuteStepsAsync("""["a();", "b();", "c();"]""", delayMs: 40));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.True(started.ElapsedMilliseconds >= 80, $"three steps paced by 40 ms took {started.ElapsedMilliseconds} ms");
    }

    /// <summary>Stopping is a human decision, not a failure: the build ends whole, says where it stopped, and keeps the globals for the rest.</summary>
    [Fact]
    public async Task StepInFromThePanelEndsTheBuildBetweenStepsAndBringsTheRemark()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);

        var codes = new List<string>();

        plugin.Handlers[EditorCommands.ExecuteCode.Name] = parameters =>
        {
            codes.Add(parameters?["code"]?.GetValue<string>() ?? string.Empty);

            if (codes.Count == 2)
            {
                _bridge.Registry.Resolve(fileKey)!.Remarks.Add(
                    new BuildInterruption("убери третий столбец", [new JsonObject { ["id"] = "1:7", ["name"] = "Bar 3" }], DateTimeOffset.UtcNow));
            }

            return new JsonObject { ["success"] = true, ["result"] = new JsonObject { ["step"] = codes.Count } };
        };

        var response = Parse(await Write().ExecuteStepsAsync("""["a();", "b();", "c();", "d();"]"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.True(response["data"]!["stopped"]!.GetValue<bool>());
        Assert.Equal(2, response["data"]!["completed"]!.GetValue<int>());
        Assert.Equal(2, codes.Count);
        Assert.Contains("step 2 of 4", response["data"]!["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("убери третий столбец", response["data"]!["remark"]!.GetValue<string>());
        Assert.Equal("Bar 3", response["data"]!["selection"]![0]!["name"]!.GetValue<string>());
    }

    private static IReadOnlyList<(int Step, int Total)> Progress(FakeEditorExtension plugin) => plugin.Received
        .Where(message => message["type"]?.GetValue<string>() == "BUILD_PROGRESS")
        .Select(message => (message["data"]!["step"]!.GetValue<int>(), message["data"]!["total"]!.GetValue<int>()))
        .ToList();

    /// <summary>Only the server can count steps: the plugin receives each one as its own command and cannot tell the second of three from the last.</summary>
    /// <remarks>Without this the panel could only guess a build was running and show a wave; with it the field shows the share and stops guessing.</remarks>
    [Fact]
    public async Task EveryStepTellsThePanelHowFarTheBuildHasGot()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        Recording(plugin);

        var response = Parse(await Write().ExecuteStepsAsync("""["a();", "b();", "c();"]"""));

        Assert.True(response["ok"]!.GetValue<bool>());

        await BridgeHostFixture.WaitUntilAsync(
            () => Progress(plugin).Contains((0, 3)),
            "the panel was never told the build was over");

        Assert.Equal([(1, 3), (2, 3), (3, 3), (0, 3)], Progress(plugin));
    }

    /// <summary>A build that dies on step two still has to clear the panel, or the field keeps a half-filled bar for a build that is long gone.</summary>
    [Fact]
    public async Task AFailedBuildStillTellsThePanelItIsOver()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        Recording(plugin, failingStep: 2);

        var response = Parse(await Write().ExecuteStepsAsync("""["first();", "second();", "third();"]"""));

        Assert.False(response["ok"]!.GetValue<bool>());

        await BridgeHostFixture.WaitUntilAsync(
            () => Progress(plugin).Contains((0, 3)),
            "the panel was left showing a build that had already failed");

        Assert.Equal([(1, 3), (2, 3), (0, 3)], Progress(plugin));
    }

    /// <summary>A remark written after the previous build is a note about its result, not a stop signal for the next one — so it lets the build run and waits for figma_get_remarks.</summary>
    [Fact]
    public async Task ARemarkLeftBeforeTheBuildLetsItRunAndStaysToBeRead()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        Recording(plugin);
        _bridge.Registry.Resolve(fileKey)!.Remarks.Add(
            new BuildInterruption("отступы гуляют", [], DateTimeOffset.UtcNow.AddSeconds(-1)));

        var response = Parse(await Write().ExecuteStepsAsync("""["a();", "b();"]"""));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(2, response["data"]!["completed"]!.GetValue<int>());
        Assert.Null(response["data"]!["stopped"]);
        Assert.Equal(1, _bridge.Registry.Resolve(fileKey)!.Remarks.Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"step":"a();"}""")]
    [InlineData("""["a();", 42]""")]
    [InlineData("[]")]
    public async Task StepsThatAreNotAListOfCodeAreRefused(string steps)
    {
        var response = Parse(await Write().ExecuteStepsAsync(steps));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MoreStepsThanOneCallRunsAreRefusedWithTheCap()
    {
        var steps = JsonSerializer.Serialize(Enumerable.Repeat("a();", ToolLimits.MaxSteps + 1));

        var response = Parse(await Write().ExecuteStepsAsync(steps));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains($"{ToolLimits.MaxSteps}", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APauseLongerThanTheCapIsRefused()
    {
        var response = Parse(await Write().ExecuteStepsAsync("""["a();"]""", delayMs: ToolLimits.MaxStepDelayMs + 1));

        Assert.False(response["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ExportNodeReturnsSvgMarkupDirectly()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
        var pixel = Convert.ToBase64String([137, 80, 78, 71]);
        AnswerScript(plugin, new JsonObject { ["format"] = "PNG", ["base64"] = pixel, ["byteLength"] = 4 });

        var response = Parse(await Session().ExportNodeAsync("1:2", "PNG", scale: 2));

        Assert.True(response["ok"]!.GetValue<bool>());
        var path = response["data"]!["path"]!.GetValue<string>();
        Assert.True(File.Exists(path), path);
        Assert.Null(response["data"]!["base64"]);
    }

    /// <summary>The platform's worker runs elsewhere: a path opens nothing there, so the picture itself has to be in the answer.</summary>
    [Fact]
    public async Task ExportNodeCarriesThePictureAsContentWhenAskedForBoth()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        var pixel = Convert.ToBase64String([137, 80, 78, 71]);
        AnswerScript(plugin, new JsonObject { ["format"] = "PNG", ["base64"] = pixel, ["byteLength"] = 4 });

        var result = await Session().ExportNodeAsync("1:2", "PNG", deliver: "both");

        Assert.Equal(2, result.Content.Count);

        var picture = Assert.IsType<ImageContentBlock>(result.Content[1]);

        Assert.Equal("image/png", picture.MimeType);
        Assert.Equal(pixel, Encoding.UTF8.GetString(picture.Data.Span));

        var response = Parse(result);

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.True(File.Exists(response["data"]!["path"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ExportNodeCarriesNoPictureByDefault()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        AnswerScript(plugin, new JsonObject
        {
            ["format"] = "PNG",
            ["base64"] = Convert.ToBase64String([137, 80, 78, 71]),
            ["byteLength"] = 4,
        });

        var result = await Session().ExportNodeAsync("1:2", "PNG");

        Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
    }

    [Fact]
    public async Task ExportNodeRejectsAnUnknownDeliveryMode()
    {
        var response = Parse(await Session().ExportNodeAsync("1:2", "PNG", deliver: "inline"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("both", response["hint"]!.GetValue<string>(), StringComparison.Ordinal);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        var urlAndFile = Parse(await Nodes().PlaceImageAsync(url: "https://x/y.png", filePath: "/tmp/pic.png"));

        Assert.False(both["ok"]!.GetValue<bool>());
        Assert.False(neither["ok"]!.GetValue<bool>());
        Assert.False(urlAndFile["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task PlaceImageBuildsTheRectangleWithAnImageFill()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
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

    /// <summary>A photograph must reach the plugin as a command parameter: inlined into the script it would be handed to the sandbox parser as source.</summary>
    [Fact]
    public async Task PlaceImageFromFileSendsBytesBesideTheScriptAndSizesTheRectangle()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);

        var scripts = new List<string>();

        plugin.Handlers[EditorCommands.ExecuteCode.Name] = parameters =>
        {
            scripts.Add(parameters?["code"]?.GetValue<string>() ?? string.Empty);

            return new JsonObject
            {
                ["success"] = true,
                ["result"] = scripts.Count == 1
                    ? new JsonObject { ["id"] = "7:1", ["parentId"] = "0:1" }
                    : new JsonObject
                    {
                        ["id"] = "7:1",
                        ["name"] = "photo",
                        ["width"] = 1200,
                        ["height"] = 800,
                        ["imageHash"] = "hash-1",
                        ["parentId"] = "0:1",
                    },
            };
        };

        plugin.Handlers[EditorCommands.SetImageFill.Name] = _ =>
            new JsonObject { ["success"] = true, ["imageHash"] = "hash-1", ["updatedCount"] = 1 };

        var images = new FakeImageFiles("QkFTRTY0");

        var response = Parse(await Nodes(images).PlaceImageAsync(filePath: "~/photos/wall.png", name: "photo"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(1200, response["data"]!["width"]!.GetValue<int>());
        Assert.Equal("~/photos/wall.png", Assert.Single(images.Requested));
        Assert.All(scripts, script => Assert.DoesNotContain("QkFTRTY0", script, StringComparison.Ordinal));

        var fill = Assert.Single(plugin.Received,
            message => message?["method"]?.GetValue<string>() == EditorCommands.SetImageFill.Name);

        Assert.Equal("QkFTRTY0", fill["params"]!["imageData"]!.GetValue<string>());
    }

    [Fact]
    public async Task PlaceImageFromFileTakesTheEmptyRectangleWithItWhenTheFillFails()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        AnswerScript(plugin, new JsonObject { ["id"] = "7:2", ["parentId"] = "0:1" });
        plugin.Errors[EditorCommands.SetImageFill.Name] = "Image fill applied to 0 node(s)";

        var response = Parse(await Nodes().PlaceImageAsync(filePath: "/tmp/wall.png"));

        Assert.False(response["ok"]!.GetValue<bool>());

        var deleted = Assert.Single(plugin.Received,
            message => message?["method"]?.GetValue<string>() == EditorCommands.DeleteNode.Name);

        Assert.Equal("7:2", deleted["params"]!["nodeId"]!.GetValue<string>());
    }

    [Fact]
    public async Task SetImageFillReadsTheFileInsteadOfTakingBytesFromTheCall()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        plugin.Handlers[EditorCommands.SetImageFill.Name] = _ =>
            new JsonObject { ["success"] = true, ["imageHash"] = "hash-2", ["updatedCount"] = 2 };

        var images = new FakeImageFiles("RlJPTUZJTEU=");

        var response = Parse(await Nodes(images)
            .SetImageFillAsync("1:2, 1:3", filePath: "/tmp/tile.png", scaleMode: "tile"));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("hash-2", response["data"]!["imageHash"]!.GetValue<string>());

        var sent = Assert.Single(plugin.Received,
            message => message?["method"]?.GetValue<string>() == EditorCommands.SetImageFill.Name)["params"]!;

        Assert.Equal("RlJPTUZJTEU=", sent["imageData"]!.GetValue<string>());
        Assert.Equal("TILE", sent["scaleMode"]!.GetValue<string>());
        Assert.Equal(2, sent["nodeIds"]!.AsArray().Count);
    }

    /// <summary>The plugin window answers a picture Figma rejected inside the reply, not as a failed command, and that text is the only clue to the cause.</summary>
    [Fact]
    public async Task PictureFigmaRejectsIsReportedWithThePluginsOwnWords()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        plugin.Handlers[EditorCommands.SetImageFill.Name] = _ =>
            new JsonObject { ["success"] = false, ["error"] = "in createImage: Image is too large" };

        var response = Parse(await Nodes().SetImageFillAsync("1:2", filePath: "/tmp/huge.png"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Image is too large", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceImageFromFileReportsTheRejectionAndLeavesNoRectangle()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
        AnswerScript(plugin, new JsonObject { ["id"] = "7:3", ["parentId"] = "0:1" });
        plugin.Handlers[EditorCommands.SetImageFill.Name] = _ =>
            new JsonObject { ["success"] = false, ["error"] = "in createImage: Image is too large" };

        var response = Parse(await Nodes().PlaceImageAsync(filePath: "/tmp/huge.png"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Image is too large", response["error"]!.GetValue<string>(), StringComparison.Ordinal);

        var deleted = Assert.Single(plugin.Received,
            message => message?["method"]?.GetValue<string>() == EditorCommands.DeleteNode.Name);

        Assert.Equal("7:3", deleted["params"]!["nodeId"]!.GetValue<string>());
    }

    [Fact]
    public async Task SetImageFillDemandsExactlyOneSource()
    {
        var both = Parse(await Nodes().SetImageFillAsync("1:2", imageData: "AA==", filePath: "/tmp/tile.png"));
        var neither = Parse(await Nodes().SetImageFillAsync("1:2"));

        Assert.False(both["ok"]!.GetValue<bool>());
        Assert.False(neither["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task JsxSpecIsEmbeddedAndBuiltThroughTheWidgetFactory()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
        await _bridge.Registry.ClaimAsync(fileKey);
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
