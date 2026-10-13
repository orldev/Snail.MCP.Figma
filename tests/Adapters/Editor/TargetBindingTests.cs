using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>The target of the commands moves only on the agent's word, and every panel learns whether this agent works in its file.</summary>
/// <remarks>Its own host, not the shared collection: the registry here has to start with no files and no target, which the shared fixture never guarantees.</remarks>
public class TargetBindingTests(BridgeHostFixture bridge) : IClassFixture<BridgeHostFixture>
{
    private static string NewFileKey() => $"f{Guid.NewGuid().ToString("N")[..10]}";

    private static bool? LastBound(FakeEditorExtension plugin) => plugin.Received
        .LastOrDefault(message => message["type"]?.GetValue<string>() == "TARGET_BOUND")?["data"]?["bound"]?.GetValue<bool>();

    private static bool HasReceived(FakeEditorExtension plugin, string type) =>
        plugin.Received.Any(message => message["type"]?.GetValue<string>() == type);

    /// <summary>Sessions of the previous test leave the registry a moment after their sockets close, and the agent keeps its target until told otherwise.</summary>
    private async Task RegistryIsEmptyAsync()
    {
        await BridgeHostFixture.WaitUntilAsync(
            () => bridge.Registry.Sessions.Count == 0,
            "the previous test's sessions did not leave the registry");
        bridge.Agent.Release();
    }

    /// <summary>Web and Mobile connected and no target chosen: the question only the agent can answer.</summary>
    private async Task<(FakeEditorExtension Web, FakeEditorExtension Mobile)> TwoFilesAndNoTargetAsync(string web, string mobile)
    {
        await RegistryIsEmptyAsync();
        var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        bridge.Agent.Release();

        return (pluginWeb, pluginMobile);
    }

    [Fact]
    public async Task TheOnlyConnectedFileBecomesTheTargetByItself()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();

        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        Assert.Equal(fileKey, bridge.Agent.ActiveFileKey);
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(plugin) == true, "the panel was not told the agent is here");
    }

    [Fact]
    public async Task ASecondFileDoesNotTakeTheTargetAway()
    {
        await RegistryIsEmptyAsync();
        var first = NewFileKey();
        var second = NewFileKey();

        await using var pluginA = await bridge.ConnectPluginAsync(first);
        await using var pluginB = await bridge.ConnectPluginAsync(second);

        Assert.Equal(first, bridge.Agent.ActiveFileKey);
        Assert.NotEqual(true, LastBound(pluginB));
    }

    [Fact]
    public async Task ClaimingTellsTheNewPanelTheAgentIsHereAndTheOldOneItLeft()
    {
        await RegistryIsEmptyAsync();
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginA = await bridge.ConnectPluginAsync(first);
        await using var pluginB = await bridge.ConnectPluginAsync(second);

        Assert.True(await bridge.Agent.TryClaimAsync(second));

        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginB) == true, "the new panel was not told");
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginA) == false, "the old panel was not told");
        Assert.Equal(second, bridge.Agent.ActiveFileKey);
    }

    [Fact]
    public async Task ClaimingAFileWithoutThePluginChangesNothing()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        Assert.False(await bridge.Agent.TryClaimAsync("file-without-plugin"));

        Assert.Equal(fileKey, bridge.Agent.ActiveFileKey);
    }

    /// <summary>A guess would write into somebody else's file, so the refusal names the choices instead.</summary>
    [Fact]
    public async Task SeveralFilesAndNoTargetRefuseACommandAndNameTheFiles()
    {
        var web = NewFileKey();
        var mobile = NewFileKey();
        var (connectedWeb, connectedMobile) = await TwoFilesAndNoTargetAsync(web, mobile);
        await using var pluginWeb = connectedWeb;
        await using var pluginMobile = connectedMobile;

        var refusal = await Assert.ThrowsAsync<EditorTargetAmbiguousException>(() => bridge.Gateway.ReadAsync(
            EditorCommands.MoveNode,
            new JsonObject { ["nodeId"] = "1:1" }));

        Assert.Contains($"Web ({web})", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"Mobile ({mobile})", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pluginWeb.Received, message => message["method"] is not null);
        Assert.DoesNotContain(pluginMobile.Received, message => message["method"] is not null);
    }

    /// <summary>The agent that opened a file by link in any tool has said where it works; that file is taken without a separate navigate.</summary>
    [Fact]
    public async Task TheFileTheAgentLastNamedIsAdoptedWhenNoTargetIsChosen()
    {
        var web = NewFileKey();
        var mobile = NewFileKey();
        var (connectedWeb, connectedMobile) = await TwoFilesAndNoTargetAsync(web, mobile);
        await using var pluginWeb = connectedWeb;
        await using var pluginMobile = connectedMobile;
        bridge.ActiveFile.Remember(mobile);

        await bridge.Gateway.ReadAsync(EditorCommands.MoveNode, new JsonObject { ["nodeId"] = "1:1" });

        Assert.Equal(mobile, bridge.Agent.ActiveFileKey);
        Assert.Single(pluginMobile.Received, message => message["method"] is not null);
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginMobile) == true, "the adopted file's panel was not told");
    }

    /// <summary>The file left behind is somebody else's: the target stays with its file, and a command says which file is missing instead of writing elsewhere.</summary>
    [Fact]
    public async Task WhenTheTargetsPluginLeavesTheTargetStaysAndACommandNamesTheFile()
    {
        await RegistryIsEmptyAsync();
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginB = await bridge.ConnectPluginAsync(second);
        await using (var pluginA = await bridge.ConnectPluginAsync(first, "Landing"))
        {
            await bridge.Agent.TryClaimAsync(first);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.Sessions.Count == 1, "the target's plugin did not leave");

        var refusal = await Assert.ThrowsAsync<EditorUnavailableException>(() => bridge.Gateway.ReadAsync(
            EditorCommands.MoveNode,
            new JsonObject { ["nodeId"] = "1:1" }));

        Assert.Equal(first, bridge.Agent.ActiveFileKey);
        Assert.Contains($"Landing ({first})", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pluginB.Received, message => message["method"] is not null);
        Assert.NotEqual(true, LastBound(pluginB));
    }

    [Fact]
    public async Task ATargetWhosePluginComesBackIsToldTheAgentIsHereAgain()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();
        var other = NewFileKey();
        await using var pluginOther = await bridge.ConnectPluginAsync(other);
        await using (var leaving = await bridge.ConnectPluginAsync(fileKey))
        {
            await bridge.Agent.TryClaimAsync(fileKey);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.Sessions.Count == 1, "the target's plugin did not leave");
        await using var back = await bridge.ConnectPluginAsync(fileKey);

        await BridgeHostFixture.WaitUntilAsync(() => LastBound(back) == true, "the returning panel was not told");
        Assert.Equal(fileKey, bridge.Agent.ActiveFileKey);
    }

    /// <summary>Every server hears every panel; a remark typed for another agent must not light this one's queue.</summary>
    [Fact]
    public async Task ARemarkTypedInAnotherAgentsFileIsNotQueued()
    {
        await RegistryIsEmptyAsync();
        var mine = NewFileKey();
        var theirs = NewFileKey();
        await using var pluginMine = await bridge.ConnectPluginAsync(mine);
        await using var pluginTheirs = await bridge.ConnectPluginAsync(theirs);
        await bridge.Agent.TryClaimAsync(mine);

        await pluginTheirs.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "not for you" });
        await pluginMine.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "for you" });

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Agent.RemarksIn(bridge.Agent.Resolve(mine)!).Count == 1, "the agent's own remark did not arrive");
        Assert.Equal(0, bridge.Agent.RemarksIn(bridge.Agent.Resolve(theirs)!).Count);
        Assert.False(HasReceived(pluginTheirs, "REMARKS_WAITING"));
    }

    [Fact]
    public async Task ThePanelNamesTheOtherAgentsInTheFile()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        await plugin.SendEventAsync("PEERS", new JsonObject { ["agents"] = new JsonArray("Keynex", "Vertical.Hub") });

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Agent.Resolve(fileKey)!.Peers.Count == 2, "the peers did not arrive");
        Assert.Equal(["Keynex", "Vertical.Hub"], bridge.Agent.Resolve(fileKey)!.Peers);
    }

    /// <summary>The refusal is data the agent can act on: the files by name and the call that settles it.</summary>
    [Fact]
    public async Task AWriteWithSeveralFilesAndNoTargetComesBackAsAReadableRefusal()
    {
        var web = NewFileKey();
        var mobile = NewFileKey();
        var (connectedWeb, connectedMobile) = await TwoFilesAndNoTargetAsync(web, mobile);
        await using var pluginWeb = connectedWeb;
        await using var pluginMobile = connectedMobile;

        var response = JsonNode.Parse(await new WriteTools(bridge.Gateway, NullLogger<WriteTools>.Instance).ExecuteAsync("return 1;"))!;

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Contains("Web", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("Mobile", response["error"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("figma_navigate", response["hint"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>A question about the human is answered from where the human is, and the answer says that no target is chosen yet.</summary>
    [Fact]
    public async Task WithoutATargetTheSelectionComesFromTheFileTheHumanTouchedLast()
    {
        var web = NewFileKey();
        var mobile = NewFileKey();
        var (connectedWeb, connectedMobile) = await TwoFilesAndNoTargetAsync(web, mobile);
        await using var pluginWeb = connectedWeb;
        await using var pluginMobile = connectedMobile;
        await pluginMobile.SendEventAsync("SELECTION_CHANGE", new JsonObject
        {
            ["count"] = 1,
            ["nodes"] = new JsonArray(new JsonObject { ["id"] = "1:9", ["name"] = "Header" }),
        });
        await BridgeHostFixture.WaitUntilAsync(() => bridge.Agent.Resolve(mobile)!.Selection is not null, "the selection did not arrive");

        var response = JsonNode.Parse(await new WriteTools(bridge.Gateway, NullLogger<WriteTools>.Instance).GetSelectionAsync())!;

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(mobile, response["fileKey"]!.GetValue<string>());
        Assert.Equal("Header", response["data"]!["nodes"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("figma_navigate", response["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(bridge.Agent.ActiveFileKey);
    }

    /// <summary>Two agents in one file are told apart by name, and the name is the folder the server was started in.</summary>
    [Fact]
    public async Task TheGreetingCarriesTheAgentsName()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();

        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        await BridgeHostFixture.WaitUntilAsync(() => HasReceived(plugin, "SERVER_HELLO"), "no greeting");
        var hello = plugin.Received.Single(message => message["type"]?.GetValue<string>() == "SERVER_HELLO");
        Assert.Equal("test-agent", hello["data"]!["agent"]!.GetValue<string>());
    }
}
