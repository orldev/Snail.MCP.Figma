using Snail.Sdk.Mcp.Hosting;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Session tools: the figma_execute_across_files fan-out, and the remark channel a human types into while the agent is elsewhere.</summary>
[Collection(nameof(BridgeHostFixture))]
public class SessionToolsTests(BridgeHostFixture bridge)
{
    private readonly BridgeHostFixture _bridge = bridge;

    private SessionTools Tools() => new(
        _bridge.Gateway,
        new ActiveDocumentTracker(),
        new CaptureStore(new ServerConfig(), NullLogger<CaptureStore>.Instance),
        NullLogger<SessionTools>.Instance);

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(result.Content[0]).Text;

    private static string[] ExecutedCode(FakeEditorExtension plugin) => [.. plugin.Received
        .Where(message => message["method"]?.GetValue<string>() == EditorCommands.ExecuteCode.Name)
        .Select(message => message["params"]!["code"]!.GetValue<string>())];

    /// <summary>Waits until only this test's sessions remain in the registry; the fixture is shared per class.</summary>
    private Task OnlyOurSessionsAsync(int expected) => BridgeHostFixture.WaitUntilAsync(
        () => _bridge.Registry.Sessions.Count(session => session.IsOpen) == expected,
        $"the registry did not end up with exactly {expected} open sessions");

    /// <summary>The channel for "the build is finished, I looked at it and found a defect": a remark left in the quiet reaches the agent through nothing else.</summary>
    [Fact]
    public async Task ARemarkLeftInThePanelComesBackOnceAndOnlyOnce()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        _bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).Add(new BuildInterruption(
            "третий столбец выше остальных",
            [new JsonObject { ["id"] = "1:7", ["name"] = "Bar 3" }],
            DateTimeOffset.UtcNow));

        var response = Parse(await Tools().GetRemarksAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(1, response["data"]!["returned"]!.GetValue<int>());
        Assert.Equal("третий столбец выше остальных", response["data"]!["remarks"]![0]!["remark"]!.GetValue<string>());
        Assert.Equal("Bar 3", response["data"]!["remarks"]![0]!["selection"]![0]!["name"]!.GetValue<string>());

        var again = Parse(await Tools().GetRemarksAsync());

        Assert.Equal(0, again["data"]!["returned"]!.GetValue<int>());
    }

    /// <summary>The agent cannot be woken, so it can end its turn waiting here and hear the human a second later instead of at the next call.</summary>
    [Fact]
    public async Task AWaitingReadReturnsAsSoonAsTheHumanWrites()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        var reading = Tools().GetRemarksAsync(waitMs: 5000);

        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            _bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).Add(
                new BuildInterruption("тут отступ гуляет", [], DateTimeOffset.UtcNow));
        });

        var response = Parse(await reading);

        Assert.Equal(1, response["data"]!["returned"]!.GetValue<int>());
        Assert.Equal("тут отступ гуляет", response["data"]!["remarks"]![0]!["remark"]!.GetValue<string>());
    }

    [Fact]
    public async Task AWaitThatHeardNothingSaysTheWaitIsOverNotBroken()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        var response = Parse(await Tools().GetRemarksAsync(waitMs: 120));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(0, response["data"]!["returned"]!.GetValue<int>());
        Assert.Contains("120 ms", response["data"]!["note"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>Reading takes the remarks, and the panel must learn it: the wave it lights on Enter goes out only on this message.</summary>
    [Fact]
    public async Task ReadingTheRemarksTellsThePanelTheQueueIsEmpty()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        _bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).Add(
            new BuildInterruption("правь отступы", [], DateTimeOffset.UtcNow));

        await Tools().GetRemarksAsync();

        await BridgeHostFixture.WaitUntilAsync(
            () => plugin.Received
                .LastOrDefault(message => message["type"]?.GetValue<string>() == "REMARKS_WAITING")?["data"]?["waiting"]?.GetValue<int>() == 0,
            "the panel was never told the queue is empty");
    }

    /// <summary>An empty answer explains itself: "nobody wrote anything" and "the panel is not there" are different facts.</summary>
    [Fact]
    public async Task AnEmptyPanelSaysNobodyWroteAnything()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        var response = Parse(await Tools().GetRemarksAsync());

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(0, response["data"]!["returned"]!.GetValue<int>());
        Assert.Contains("nobody has left a remark", response["data"]!["note"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedFilesEachRunTheCodeInTheirOwnContext()
    {
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(first, "Web");
        await using var pluginB = await _bridge.ConnectPluginAsync(second, "Mobile");

        pluginA.Handlers[EditorCommands.ExecuteCode.Name] = _ => new JsonObject { ["pageCount"] = 3 };
        pluginB.Handlers[EditorCommands.ExecuteCode.Name] = _ => new JsonObject { ["pageCount"] = 7 };

        var response = Parse(await Tools().ExecuteAcrossFilesAsync(
            "return figma.root.children.length;",
            $"{first}, {second}"));

        Assert.True(response["ok"]!.GetValue<bool>());

        var data = response["data"]!;
        Assert.Equal(2, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal(2, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Equal(0, data["summary"]!["failed"]!.GetValue<int>());

        Assert.Equal(3, data["results"]![first]!["result"]!["pageCount"]!.GetValue<int>());
        Assert.Equal(7, data["results"]![second]!["result"]!["pageCount"]!.GetValue<int>());
        Assert.Equal("Web", data["results"]![first]!["file_name"]!.GetValue<string>());

        Assert.Equal(["return figma.root.children.length;"], ExecutedCode(pluginA));
        Assert.Equal(["return figma.root.children.length;"], ExecutedCode(pluginB));
    }

    /// <summary>The agent learns who shares the file at the moment it takes it, so it can keep to its own page or section.</summary>
    [Fact]
    public async Task NavigateNamesTheOtherAgentsInTheFile()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await plugin.SendEventAsync("PEERS", new JsonObject { ["agents"] = new JsonArray("Keynex") });
        await BridgeHostFixture.WaitUntilAsync(() => _bridge.Agent.Resolve(fileKey)!.Peers.Count == 1, "the peers did not arrive");

        var response = Parse(await Tools().NavigateAsync(fileKey));

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.True(response["data"]!["pluginConnected"]!.GetValue<bool>());
        Assert.Equal("Keynex", response["data"]!["othersHere"]![0]!.GetValue<string>());
        Assert.Contains("Keynex", response["data"]!["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(fileKey, _bridge.Agent.ActiveFileKey);
    }

    [Fact]
    public async Task ListingTheFilesMarksTheTargetAndItsCompany()
    {
        var target = NewFileKey();
        var other = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(target, "Web");
        await using var pluginB = await _bridge.ConnectPluginAsync(other, "Mobile");
        await _bridge.Agent.TryClaimAsync(target);
        await pluginA.SendEventAsync("PEERS", new JsonObject { ["agents"] = new JsonArray("Keynex") });
        await BridgeHostFixture.WaitUntilAsync(() => _bridge.Agent.Resolve(target)!.Peers.Count == 1, "the peers did not arrive");

        var files = Parse(Text(await Tools().ListOpenFilesAsync()))["data"]!["files"]!.AsArray();

        var web = files.Single(file => file!["fileKey"]!.GetValue<string>() == target)!;
        var mobile = files.Single(file => file!["fileKey"]!.GetValue<string>() == other)!;
        Assert.True(web["target"]!.GetValue<bool>());
        Assert.Equal("Keynex", web["othersHere"]![0]!.GetValue<string>());
        Assert.False(mobile["target"]!.GetValue<bool>());
        Assert.Empty(mobile["othersHere"]!.AsArray());
    }

    [Fact]
    public async Task TheTargetSurvivesTheFanOut()
    {
        var target = NewFileKey();
        var other = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(target);
        await using var pluginB = await _bridge.ConnectPluginAsync(other);
        await _bridge.Agent.TryClaimAsync(target);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", other))["data"]!;

        Assert.Equal(target, data["active_file_key"]!.GetValue<string>());
        Assert.Equal(target, _bridge.Agent.ActiveFileKey);
        Assert.Empty(ExecutedCode(pluginA));
        Assert.Single(ExecutedCode(pluginB));
    }

    [Fact]
    public async Task TargetsMustBeStatedExplicitly()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;"));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("allFiles", response["hint"]!.GetValue<string>());
        Assert.Contains("figma_list_open_files", response["hint"]!.GetValue<string>());

        Assert.Empty(ExecutedCode(plugin));
    }

    [Fact]
    public async Task AllFilesReachesEveryConnectedFile()
    {
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(first);
        await using var pluginB = await _bridge.ConnectPluginAsync(second);
        await OnlyOurSessionsAsync(2);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", allFiles: true))["data"]!;

        Assert.Equal(2, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal(2, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Single(ExecutedCode(pluginA));
        Assert.Single(ExecutedCode(pluginB));
    }

    [Fact]
    public async Task OneFailingFileDoesNotSinkTheRest()
    {
        var healthy = NewFileKey();
        var broken = NewFileKey();
        await using var pluginA = await _bridge.ConnectPluginAsync(healthy);
        await using var pluginB = await _bridge.ConnectPluginAsync(broken);

        pluginB.Errors[EditorCommands.ExecuteCode.Name] = "ReferenceError: foo is not defined";

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return foo;", $"{healthy},{broken}"));

        Assert.True(response["ok"]!.GetValue<bool>());

        var data = response["data"]!;
        Assert.Equal(1, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Equal(1, data["summary"]!["failed"]!.GetValue<int>());
        Assert.True(data["results"]![healthy]!["success"]!.GetValue<bool>());
        Assert.False(data["results"]![broken]!["success"]!.GetValue<bool>());
        Assert.Contains("ReferenceError", data["results"]![broken]!["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task TotalFailureStillCarriesThePerFileBreakdown()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Errors[EditorCommands.ExecuteCode.Name] = "TypeError: nope";

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return nope;", fileKey));

        Assert.False(response["ok"]!.GetValue<bool>());

        Assert.Contains("TypeError", response["data"]!["results"]![fileKey]!["error"]!.GetValue<string>());
        Assert.Equal(0, response["data"]!["summary"]!["succeeded"]!.GetValue<int>());
    }

    [Fact]
    public async Task DisconnectedFileKeysAreReportedInsteadOfSilentlyDropped()
    {
        var connected = NewFileKey();
        var closed = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", $"{connected},{closed}"))["data"]!;

        Assert.Equal(1, data["summary"]!["targeted"]!.GetValue<int>());
        Assert.Equal([closed], [.. data["missing_file_keys"]!.AsArray().Select(key => key!.GetValue<string>())]);
    }

    [Fact]
    public async Task NoneOfTheNamedFilesConnectedIsAnErrorNotAnEmptySuccess()
    {
        var connected = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(connected);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", NewFileKey()));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("None of the named files", response["error"]!.GetValue<string>());
        Assert.Empty(ExecutedCode(plugin));
    }

    [Fact]
    public async Task FileUrlsAreAcceptedWhereFileKeysAre()
    {
        var fileKey = "ABC123XYZ";
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        var data = Parse(await Tools().ExecuteAcrossFilesAsync(
            "return 1;",
            $"https://www.figma.com/design/{fileKey}/Design-System"))["data"]!;

        Assert.Equal(1, data["summary"]!["succeeded"]!.GetValue<int>());
        Assert.Single(ExecutedCode(plugin));
    }

    [Fact]
    public async Task WithoutAnyPluginTheAnswerIsTheUsualBridgeHint()
    {
        await OnlyOurSessionsAsync(0);

        var response = Parse(await Tools().ExecuteAcrossFilesAsync("return 1;", allFiles: true));

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Snail Bridge", response["error"]!.GetValue<string>());
    }

    /// <summary>The structured answer is what a code-mode host validates against the advertised schema, so its keys, down to one file, must be exactly the schema's.</summary>
    [Fact]
    public async Task ListingTheFilesAnswersStructuredContentShapedLikeItsOutputSchema()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        var result = await Tools().ListOpenFilesAsync();
        var schema = OutputSchemaOf(nameof(SessionTools.ListOpenFilesAsync));

        var structured = Assert.NotNull(result.StructuredContent);
        Assert.Equal(Text(result), structured.GetRawText());
        AssertShapedLike(schema, structured);
    }

    private static JsonElement OutputSchemaOf(string method) =>
        McpServerTool.Create(typeof(SessionTools).GetMethod(method)!, _ => throw new NotSupportedException(), new McpServerToolCreateOptions { SerializerOptions = ToolJson.Options(ToolJsonContext.Default) })
            .ProtocolTool.OutputSchema!.Value;

    /// <summary>Every object in the answer carries exactly the properties its schema lists; arrays are checked through their first element.</summary>
    private static void AssertShapedLike(JsonElement schema, JsonElement instance)
    {
        if (schema.TryGetProperty("properties", out var properties))
        {
            var expected = properties.EnumerateObject().Select(property => property.Name).Order().ToList();

            Assert.Equal(expected, [.. instance.EnumerateObject().Select(property => property.Name).Order()]);

            foreach (var property in properties.EnumerateObject())
            {
                AssertShapedLike(property.Value, instance.GetProperty(property.Name));
            }

            return;
        }

        if (schema.TryGetProperty("items", out var items) && instance.ValueKind == JsonValueKind.Array && instance.GetArrayLength() > 0)
        {
            AssertShapedLike(items, instance[0]);
        }
    }

    /// <summary>The console belongs to the file and other agents may be reading it: clearing starts this agent's reading from now and empties nothing.</summary>
    [Fact]
    public async Task ClearingTheConsoleStartsThisAgentsReadingFromNowAndKeepsTheLines()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        var session = _bridge.Agent.Resolve(fileKey)!;
        await plugin.SendEventAsync("CONSOLE_CAPTURE", new JsonObject { ["level"] = "log", ["message"] = "before one" });
        await plugin.SendEventAsync("CONSOLE_CAPTURE", new JsonObject { ["level"] = "log", ["message"] = "before two" });
        await BridgeHostFixture.WaitUntilAsync(() => session.ConsoleLogs.Count == 2, "the console lines did not arrive");

        await Tools().ClearConsoleAsync();
        var afterClearing = Parse(await Tools().GetConsoleLogsAsync());
        await plugin.SendEventAsync("CONSOLE_CAPTURE", new JsonObject { ["level"] = "log", ["message"] = "after" });
        await BridgeHostFixture.WaitUntilAsync(() => session.ConsoleLogs.Count == 3, "the new line did not arrive");
        var afterNewLine = Parse(await Tools().GetConsoleLogsAsync());

        Assert.Equal(0, afterClearing["data"]!["returned"]!.GetValue<int>());
        Assert.Equal(["after"], afterNewLine["data"]!["logs"]!.AsArray().Select(line => line!["message"]!.GetValue<string>()));
        Assert.Equal(3, session.ConsoleLogs.Count);
    }
}
