using Snail.MCP.Figma.Adapters.Editor;

using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Stage 2 definition of done: "move the node 10 pixels right" travels to the plugin and back.</summary>
[Collection(nameof(BridgeHostFixture))]
public class EditorSocketHubTests
{
    private readonly BridgeHostFixture _bridge;

    public EditorSocketHubTests(BridgeHostFixture bridge) => _bridge = bridge;

    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    [Fact]
    public async Task PluginIdentifiesItselfAndBecomesTheActiveFile()
    {
        var fileKey = NewFileKey();
        await using var plugin = await _bridge.ConnectPluginAsync(fileKey, "Design System");

        var session = _bridge.Registry.Resolve(fileKey);
        Assert.NotNull(session);
        Assert.Equal("Design System", session!.FileName);
        Assert.Equal("1.40.0", session.PluginVersion);
        Assert.True(_bridge.Gateway.IsConnected);

        await BridgeHostFixture.WaitUntilAsync(
            () => plugin.Received.Any(m => m["type"]?.GetValue<string>() == "SERVER_HELLO"),
            "plugin did not receive SERVER_HELLO");
    }

    [Fact]
    public async Task MoveNodeTravelsToThePluginAndTheResultComesBack()
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
    public async Task ConcurrentCommandsAreMatchedByRequestId()
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
    public async Task PluginErrorSurfacesAsACommandFailure()
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
    public async Task CommandsFailFastWhenNoPluginIsConnected()
    {
        await Assert.ThrowsAsync<EditorUnavailableException>(() => _bridge.Gateway.ReadAsync(
            EditorCommands.MoveNode,
            new JsonObject { ["nodeId"] = "1:1" },
            "file-without-plugin"));
    }

    [Fact]
    public async Task SilentPluginProducesATimeoutRatherThanAHang()
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
    public async Task SelectionEventUpdatesTheSessionAndTheActiveFile()
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
            () => _bridge.Registry.Resolve(fileKey)?.Selection is not null,
            "selection did not reach the session");

        var selection = _bridge.Registry.Resolve(fileKey)!.Selection!;
        Assert.Equal("Button", selection["nodes"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(fileKey, _bridge.Registry.ActiveFileKey);
    }

    [Fact]
    public async Task DocumentChangeIsBufferedOnTheSession()
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
            () => _bridge.Registry.Resolve(fileKey)?.DocumentChanges.Count > 0,
            "document change event did not arrive");

        var change = _bridge.Registry.Resolve(fileKey)!.DocumentChanges.Tail(1)[0];
        Assert.Equal(2, change["changeCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task LockedTargetIgnoresUserInteractionInAnotherFile()
    {
        var agentFile = NewFileKey();
        var userFile = NewFileKey();

        await using var agentPlugin = await _bridge.ConnectPluginAsync(agentFile, "Agent file");
        await using var userPlugin = await _bridge.ConnectPluginAsync(userFile, "User file");

        Assert.True(_bridge.Registry.SetActiveFile(agentFile, @lock: true));

        await userPlugin.SendEventAsync("SELECTION_CHANGE", new JsonObject { ["count"] = 1 });
        await BridgeHostFixture.WaitUntilAsync(
            () => _bridge.Registry.Resolve(userFile)?.Selection is not null,
            "selection did not arrive");

        Assert.Equal(agentFile, _bridge.Registry.ActiveFileKey);

        _bridge.Registry.Unlock();
    }

    [Fact]
    public async Task VariablesSnapshotIsStoredAndServedWithoutASocketRoundTrip()
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
            () => _bridge.Registry.Resolve(fileKey)?.VariablesSnapshot is not null,
            "variables snapshot did not arrive");

        var served = await _bridge.Gateway.ReadAsync(
            Snail.MCP.Figma.Application.Editor.EditorCommands.GetVariablesData, fileKey: fileKey);

        Assert.Equal("bg/lowest", served!["variables"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task AStaleSessionGetsTheUpdateBanner()
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
    public async Task AMatchingSessionGetsNoBanner()
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
