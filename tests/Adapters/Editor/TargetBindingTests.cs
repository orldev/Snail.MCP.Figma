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

    /// <summary>Sessions of the previous test leave the registry a moment after their sockets close.</summary>
    private Task RegistryIsEmptyAsync() => BridgeHostFixture.WaitUntilAsync(
        () => bridge.Registry.Sessions.Count == 0,
        "the previous test's sessions did not leave the registry");

    [Fact]
    public async Task TheOnlyConnectedFileBecomesTheTargetByItself()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();

        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        Assert.Equal(fileKey, bridge.Registry.ActiveFileKey);
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

        Assert.Equal(first, bridge.Registry.ActiveFileKey);
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

        Assert.True(await bridge.Registry.TryClaimAsync(second));

        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginB) == true, "the new panel was not told");
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginA) == false, "the old panel was not told");
        Assert.Equal(second, bridge.Registry.ActiveFileKey);
    }

    [Fact]
    public async Task ClaimingAFileWithoutThePluginChangesNothing()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        Assert.False(await bridge.Registry.TryClaimAsync("file-without-plugin"));

        Assert.Equal(fileKey, bridge.Registry.ActiveFileKey);
    }

    /// <summary>A guess would write into somebody else's file, so the refusal names the choices instead.</summary>
    [Fact]
    public async Task SeveralFilesAndNoTargetRefuseACommandAndNameTheFiles()
    {
        await RegistryIsEmptyAsync();
        var leaving = NewFileKey();
        var web = NewFileKey();
        var mobile = NewFileKey();
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await using (var pluginLeaving = await bridge.ConnectPluginAsync(leaving))
        {
            await bridge.Registry.TryClaimAsync(leaving);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.ActiveFileKey is null, "the target did not go away with its plugin");

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
        await RegistryIsEmptyAsync();
        var leaving = NewFileKey();
        var web = NewFileKey();
        var mobile = NewFileKey();
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await using (var pluginLeaving = await bridge.ConnectPluginAsync(leaving))
        {
            await bridge.Registry.TryClaimAsync(leaving);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.ActiveFileKey is null, "the target did not go away with its plugin");
        bridge.ActiveFile.Remember(mobile);

        await bridge.Gateway.ReadAsync(EditorCommands.MoveNode, new JsonObject { ["nodeId"] = "1:1" });

        Assert.Equal(mobile, bridge.Registry.ActiveFileKey);
        Assert.Single(pluginMobile.Received, message => message["method"] is not null);
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginMobile) == true, "the adopted file's panel was not told");
    }

    [Fact]
    public async Task WhenTheTargetsPluginLeavesTheOnlyFileLeftTakesOver()
    {
        await RegistryIsEmptyAsync();
        var first = NewFileKey();
        var second = NewFileKey();
        await using var pluginB = await bridge.ConnectPluginAsync(second);
        await using (var pluginA = await bridge.ConnectPluginAsync(first))
        {
            await bridge.Registry.TryClaimAsync(first);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.ActiveFileKey == second, "the remaining file did not take over");
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(pluginB) == true, "its panel was not told");
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
        await bridge.Registry.TryClaimAsync(mine);

        await pluginTheirs.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "not for you" });
        await pluginMine.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "for you" });

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.Resolve(mine)!.Remarks.Count == 1, "the agent's own remark did not arrive");
        Assert.Equal(0, bridge.Registry.Resolve(theirs)!.Remarks.Count);
        Assert.False(HasReceived(pluginTheirs, "REMARKS_WAITING"));
    }

    [Fact]
    public async Task ThePanelNamesTheOtherAgentsInTheFile()
    {
        await RegistryIsEmptyAsync();
        var fileKey = NewFileKey();
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        await plugin.SendEventAsync("PEERS", new JsonObject { ["agents"] = new JsonArray("Keynex", "Vertical.Hub") });

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.Resolve(fileKey)!.Peers.Count == 2, "the peers did not arrive");
        Assert.Equal(["Keynex", "Vertical.Hub"], bridge.Registry.Resolve(fileKey)!.Peers);
    }

    /// <summary>The refusal is data the agent can act on: the files by name and the call that settles it.</summary>
    [Fact]
    public async Task AWriteWithSeveralFilesAndNoTargetComesBackAsAReadableRefusal()
    {
        await RegistryIsEmptyAsync();
        var leaving = NewFileKey();
        var web = NewFileKey();
        var mobile = NewFileKey();
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await using (var pluginLeaving = await bridge.ConnectPluginAsync(leaving))
        {
            await bridge.Registry.TryClaimAsync(leaving);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.ActiveFileKey is null, "the target did not go away with its plugin");

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
        await RegistryIsEmptyAsync();
        var leaving = NewFileKey();
        var web = NewFileKey();
        var mobile = NewFileKey();
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await using (var pluginLeaving = await bridge.ConnectPluginAsync(leaving))
        {
            await bridge.Registry.TryClaimAsync(leaving);
        }

        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.ActiveFileKey is null, "the target did not go away with its plugin");
        await pluginMobile.SendEventAsync("SELECTION_CHANGE", new JsonObject
        {
            ["count"] = 1,
            ["nodes"] = new JsonArray(new JsonObject { ["id"] = "1:9", ["name"] = "Header" }),
        });
        await BridgeHostFixture.WaitUntilAsync(() => bridge.Registry.Resolve(mobile)!.Selection is not null, "the selection did not arrive");

        var response = JsonNode.Parse(await new WriteTools(bridge.Gateway, NullLogger<WriteTools>.Instance).GetSelectionAsync())!;

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal(mobile, response["fileKey"]!.GetValue<string>());
        Assert.Equal("Header", response["data"]!["nodes"]![0]!["name"]!.GetValue<string>());
        Assert.Contains("figma_navigate", response["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Null(bridge.Registry.ActiveFileKey);
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
