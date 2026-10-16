using Snail.MCP.Figma.Adapters.Editor;

using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Stage 2 definition of done: "move the node 10 pixels right" travels to the plugin and back.</summary>
[Collection(nameof(BridgeHostFixture))]
public class EditorSocketHubTests(BridgeHostFixture bridge)
{
    private readonly BridgeHostFixture _bridge = bridge;

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    [Fact]
    public async Task EditorSocketHub_PluginIdentifiesItself_BecomesTheActiveFile()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey, "Design System");

        var session = _bridge.Agent.Resolve(fileKey);
        Assert.NotNull(session);
        Assert.Equal("Design System", session!.FileName);
        Assert.Equal("1.40.0", session.PluginVersion);
        Assert.True(_bridge.Gateway.IsConnected);

        await BridgeHostFixture.WaitUntilAsync(
            () => plugin.Received.Any(m => m["type"]?.GetValue<string>() == "SERVER_HELLO"),
            "plugin did not receive SERVER_HELLO");
    }

    [Fact]
    public async Task EditorSocketHub_MoveNodeSent_ResultComesBackFromThePlugin()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Handlers[EditorCommands.MoveNode.Name] = parameters => new JsonObject
        {
            ["success"] = true,
            ["nodeId"] = parameters?["nodeId"]?.DeepClone(),
            ["x"] = parameters?["x"]?.DeepClone(),
            ["y"] = parameters?["y"]?.DeepClone(),
            ["parentId"] = "1:1",
        };

        var result = await _bridge.Gateway.ApplyAsync(
            EditorCommands.MoveNode,
            new JsonObject { ["nodeId"] = "1:234", ["x"] = 110.0, ["y"] = 40.0 },
            fileKey);

        Assert.True(result!["success"]!.GetValue<bool>());
        Assert.Equal("1:234", result["nodeId"]!.GetValue<string>());
        Assert.Equal(110.0, result["x"]!.GetValue<double>());

        var sent = plugin.Received.Single(m => m["method"]?.GetValue<string>() == EditorCommands.MoveNode.Name);
        Assert.False(string.IsNullOrWhiteSpace(sent["id"]?.GetValue<string>()));
        Assert.Equal("1:234", sent["params"]!["nodeId"]!.GetValue<string>());
    }

    [Fact]
    public async Task EditorSocketHub_ConcurrentCommands_AreMatchedByRequestId()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Handlers[EditorCommands.RenameNode.Name] = parameters => new JsonObject
        {
            ["echo"] = parameters?["newName"]?.DeepClone(),
        };

        var calls = Enumerable.Range(0, 12).Select(i => _bridge.Gateway.ReadAsync(
            EditorCommands.RenameNode,
            new JsonObject { ["nodeId"] = $"1:{i}", ["newName"] = $"Node {i}" },
            fileKey));

        var results = await Task.WhenAll(calls);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal($"Node {i}", results[i]!["echo"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task EditorSocketHub_PluginAnswersWithAnError_CommandFails()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Errors[EditorCommands.DeleteNode.Name] = "Node not found: 9:999";

        var failure = await Assert.ThrowsAsync<EditorCommandException>(() => _bridge.Gateway.ReadAsync(
            EditorCommands.DeleteNode,
            new JsonObject { ["nodeId"] = "9:999" },
            fileKey));

        Assert.Contains("Node not found", failure.Message);
    }

    [Fact]
    public async Task EditorSocketHub_NoPluginConnected_CommandsFailFast()
    {
        await Assert.ThrowsAsync<EditorUnavailableException>(() => _bridge.Gateway.ReadAsync(
            EditorCommands.MoveNode,
            new JsonObject { ["nodeId"] = "1:1" },
            "file-without-plugin"));
    }

    [Fact]
    public async Task EditorSocketHub_SilentPlugin_TimesOutRatherThanHangs()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        plugin.Handlers[EditorCommands.CaptureScreenshot.Name] = _ => throw new OperationCanceledException();

        await Assert.ThrowsAsync<TimeoutException>(() => _bridge.Gateway.ReadAsync(
            EditorCommands.CaptureScreenshot,
            new JsonObject { ["nodeId"] = "1:1" },
            fileKey,
            TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public async Task EditorSocketHub_SelectionEvent_UpdatesTheSessionAndStampsTheInteraction()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        await plugin.SendEventAsync("SELECTION_CHANGE", new JsonObject
        {
            ["count"] = 1,
            ["page"] = "Page 1",
            ["nodes"] = new JsonArray(new JsonObject
            {
                ["id"] = "1:234",
                ["name"] = "Button",
                ["type"] = "INSTANCE",
                ["width"] = 120,
                ["height"] = 40,
            }),
        });

        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.Selection is not null,
            "selection did not reach the session");

        var session = _bridge.Agent.Resolve(fileKey)!;
        Assert.Equal("Button", session.Selection!["nodes"]![0]!["name"]!.GetValue<string>());
        Assert.True(session.LastInteractionAt > session.ConnectedAt.AddSeconds(-1));
    }

    /// <summary>The panel is the human's only way into a build that is already running, and the remark is what makes the interruption worth more than a bare stop.</summary>
    [Fact]
    public async Task EditorSocketHub_StepInFromThePanel_CarriesTheRemarkAndTheSelection()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        await plugin.SendEventAsync("BUILD_STOP", new JsonObject
        {
            ["remark"] = "убери третий столбец",
            ["nodes"] = new JsonArray(new JsonObject { ["id"] = "1:7", ["name"] = "Bar 3", ["type"] = "RECTANGLE" }),
        });

        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey) is { } session && _bridge.Agent.RemarksIn(session).Count > 0,
            "the remark did not arrive");

        var stepIn = Assert.Single(_bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).TakeAll());

        Assert.Equal("убери третий столбец", stepIn.Remark);
        Assert.Equal("Bar 3", Assert.Single(stepIn.Nodes)["name"]!.GetValue<string>());
        Assert.Equal(0, _bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).Count);
    }

    /// <summary>The queue lives on the server, so the panel has to be told the count — otherwise the wave it lights on Enter would never go out.</summary>
    [Fact]
    public async Task EditorSocketHub_RemarksQueued_PanelIsToldHowManyWait()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "отступы гуляют" });

        await BridgeHostFixture.WaitUntilAsync(
            () => Waiting(plugin) == 1,
            "the panel was never told about the waiting remark");
    }

    [Fact]
    public async Task EditorSocketHub_RemarksTakenBack_EmptiesTheQueueAndSaysSo()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);
        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "передумал" });
        await BridgeHostFixture.WaitUntilAsync(() => Waiting(plugin) == 1, "the remark did not arrive");

        await plugin.SendEventAsync("REMARKS_CLEAR", []);

        await BridgeHostFixture.WaitUntilAsync(() => Waiting(plugin) == 0, "the queue was not taken back");

        Assert.Equal(0, _bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).Count);
    }

    private static int? Waiting(FakeEditorExtension plugin) => plugin.Received
        .LastOrDefault(message => message["type"]?.GetValue<string>() == "REMARKS_WAITING")?["data"]?["waiting"]?.GetValue<int>();

    [Fact]
    public async Task EditorSocketHub_StopWithoutWords_IsStillAnInterruption()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await _bridge.Agent.TryClaimAsync(fileKey);

        await plugin.SendEventAsync("BUILD_STOP", []);

        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey) is { } session && _bridge.Agent.RemarksIn(session).Count > 0,
            "the remark did not arrive");

        var stepIn = Assert.Single(_bridge.Agent.RemarksIn(_bridge.Agent.Resolve(fileKey)!).TakeAll());

        Assert.Null(stepIn.Remark);
        Assert.Empty(stepIn.Nodes);
    }

    [Fact]
    public async Task EditorSocketHub_DocumentChange_IsBufferedOnTheSession()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

        await plugin.SendEventAsync("DOCUMENT_CHANGE", new JsonObject
        {
            ["hasNodeChanges"] = true,
            ["hasStyleChanges"] = false,
            ["changedNodeIds"] = new JsonArray("1:234", "1:235"),
            ["changeCount"] = 2,
        });

        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.DocumentChanges.Count > 0,
            "document change event did not arrive");

        var change = _bridge.Agent.Resolve(fileKey)!.DocumentChanges.Tail(1)[0];
        Assert.Equal(2, change["changeCount"]!.GetValue<int>());
    }

    /// <summary>The human reviewing another agent's file must not redirect this one's build: a click is not an instruction to the server.</summary>
    [Fact]
    public async Task EditorSocketHub_ClickInAnotherFile_LeavesTheTargetInPlace()
    {
        var agentFile = NewFileKey();
        var userFile = NewFileKey();

        await using var agentPlugin = await _bridge.ConnectPluginAsync(agentFile, "Agent file");
        await using var userPlugin = await _bridge.ConnectPluginAsync(userFile, "User file");

        Assert.True(await _bridge.Agent.TryClaimAsync(agentFile));

        await userPlugin.SendEventAsync("SELECTION_CHANGE", new JsonObject { ["count"] = 1 });
        await userPlugin.SendEventAsync("PAGE_CHANGE", new JsonObject { ["pageId"] = "1:1", ["pageName"] = "Page 2" });
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(userFile)?.CurrentPage == "Page 2",
            "the events did not arrive");

        Assert.Equal(agentFile, _bridge.Agent.ActiveFileKey);
    }

    [Fact]
    public async Task EditorSocketHub_VariablesSnapshot_IsServedWithoutASocketRoundTrip()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        plugin.Errors["GET_VARIABLES_DATA"] = "the snapshot must be served locally, not over the socket";

        await plugin.SendEventAsync("VARIABLES_DATA", new JsonObject
        {
            ["success"] = true,
            ["variables"] = new JsonArray(new JsonObject { ["id"] = "V:1", ["name"] = "bg/lowest" }),
            ["variableCollections"] = new JsonArray(),
        });
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.VariablesSnapshot is not null,
            "variables snapshot did not arrive");

        var served = await _bridge.Gateway.ReadAsync(
            EditorCommands.GetVariablesData, fileKey: fileKey);

        Assert.Equal("bg/lowest", served!["variables"]![0]!["name"]!.GetValue<string>());
    }

    /// <summary>A collection created after the plugin started never reached the snapshot, so a read after a write answered with the file as it was at start-up.</summary>
    [Fact]
    public async Task EditorSocketHub_WriteApplied_NextVariablesReadAsksThePluginAgain()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await plugin.SendEventAsync("VARIABLES_DATA", PluginVariables.Payload("color/old"));
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.HasFreshVariables == true,
            "variables snapshot did not arrive");
        plugin.Handlers[EditorCommands.RefreshVariables.Name] = _ => new JsonObject
        {
            ["success"] = true,
            ["data"] = PluginVariables.Payload("color/new"),
        };

        await _bridge.Gateway.ApplyAsync(EditorCommands.RenameNode, new JsonObject { ["nodeId"] = "1:2" }, fileKey: fileKey);
        var served = await _bridge.Gateway.ReadAsync(EditorCommands.GetVariablesData, fileKey: fileKey);

        Assert.Equal("color/new", served!["variables"]![0]!["name"]!.GetValue<string>());
        Assert.True(_bridge.Agent.Resolve(fileKey)!.HasFreshVariables);
    }

    [Fact]
    public async Task EditorSocketHub_HumanEditsTheFile_VariablesSnapshotIsDoubted()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await plugin.SendEventAsync("VARIABLES_DATA", PluginVariables.Payload());
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.HasFreshVariables == true,
            "variables snapshot did not arrive");

        await plugin.SendEventAsync("DOCUMENT_CHANGE", new JsonObject
        {
            ["hasStyleChanges"] = false,
            ["changedNodeIds"] = new JsonArray("1:2"),
        });

        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.HasFreshVariables == false,
            "the document change did not doubt the snapshot");
    }

    /// <summary>Figma reports no event for a variable edited in its own panel, so the snapshot also stops being believed by age alone.</summary>
    [Fact]
    public async Task EditorSession_UntouchedVariablesSnapshot_StopsBeingBelievedAfterHalfAMinute()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
        await plugin.SendEventAsync("VARIABLES_DATA", PluginVariables.Payload());
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Agent.Resolve(fileKey)?.HasFreshVariables == true,
            "variables snapshot did not arrive");

        var session = _bridge.Agent.Resolve(fileKey)!;

        Assert.True(session.HasFreshVariablesAt(DateTimeOffset.UtcNow + EditorSession.VariablesBelievedFor - TimeSpan.FromSeconds(5)));
        Assert.False(session.HasFreshVariablesAt(DateTimeOffset.UtcNow + EditorSession.VariablesBelievedFor));
    }

    [Fact]
    public async Task EditorSocketHub_StalePluginVersion_SendsTheUpdateBanner()
    {
        _bridge.Hub.ExpectedPluginVersion = "9.9.9-test";

        try
        {
            var fileKey = NewFileKey();
            await using var plugin = await _bridge.ConnectPluginAsync(fileKey);

            await BridgeHostFixture.WaitUntilAsync(
                () => plugin.Received.Any(m => m["type"]?.GetValue<string>() == "PLUGIN_UPDATE_AVAILABLE"),
                "the update banner message did not arrive");
        }
        finally
        {
            _bridge.Hub.ExpectedPluginVersion = null;
        }
    }

    [Fact]
    public async Task EditorSocketHub_MatchingPluginVersion_SendsNoBanner()
    {
        _bridge.Hub.ExpectedPluginVersion = "1.40.0";

        try
        {
            var fileKey = NewFileKey();
            await using var plugin = await _bridge.ConnectPluginAsync(fileKey);
            await Task.Delay(100);

            Assert.DoesNotContain(plugin.Received, m => m["type"]?.GetValue<string>() == "PLUGIN_UPDATE_AVAILABLE");
        }
        finally
        {
            _bridge.Hub.ExpectedPluginVersion = null;
        }
    }
}
