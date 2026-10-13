using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Protocol 2: one connection carries several agents, every message about an agent names it, and each agent's commands take their turn.</summary>
/// <remarks>A shared host, as a panel of protocol 2 meets it; every test opens sessions of its own.</remarks>
public class AgentsProtocolTests(SharedBridgeHostFixture bridge) : IClassFixture<SharedBridgeHostFixture>
{
    private const int Protocol = 2;

    private static string NewKey(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..10]}";

    /// <summary>The last word of the server about one agent, as the panel keeps it.</summary>
    private static JsonNode? Described(FakeEditorExtension plugin, string agent) => plugin.Received
        .LastOrDefault(message => message["type"]?.GetValue<string>() == "AGENTS")?["data"]?["agents"]?.AsArray()
        .FirstOrDefault(entry => entry?["id"]?.GetValue<string>() == agent);

    private static int Commands(FakeEditorExtension plugin) =>
        plugin.Received.Count(message => message["method"] is not null);

    private async Task<AgentSlot> ClaimAsync(string session, string fileKey)
    {
        using var serving = bridge.Roster.Serve(session, "claude-code");
        Assert.True(await bridge.Gateway.Documents.TryClaimAsync(fileKey));

        return bridge.Roster.Current;
    }

    private Task<JsonNode?> MoveAsync(string session)
    {
        using var serving = bridge.Roster.Serve(session, "claude-code");

        return bridge.Gateway.ApplyAsync(EditorCommands.MoveNode, new JsonObject { ["nodeId"] = "1:1" });
    }

    [Fact]
    public async Task ThePanelIsToldEveryAgentByNameAndWhereEachWorks()
    {
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);

        var working = await ClaimAsync(NewKey("s"), fileKey);
        var elsewhere = bridge.Roster.Open(NewKey("s"), "Vertical.Hub");

        await BridgeHostFixture.WaitUntilAsync(() => Described(plugin, elsewhere.Id) is not null, "the panel was not told about the second agent");
        Assert.True(Described(plugin, working.Id)!["bound"]!.GetValue<bool>());
        Assert.False(Described(plugin, elsewhere.Id)!["bound"]!.GetValue<bool>());
        Assert.Equal("Vertical.Hub", Described(plugin, elsewhere.Id)!["name"]!.GetValue<string>());
        Assert.DoesNotContain(plugin.Received, message => message["type"]?.GetValue<string>() == "TARGET_BOUND");
    }

    [Fact]
    public async Task ARemarkAddressedToOneOfTwoAgentsInTheFileReachesOnlyIt()
    {
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var addressed = await ClaimAsync(NewKey("s"), fileKey);
        var other = await ClaimAsync(NewKey("s"), fileKey);
        var session = bridge.Registry.Open(fileKey)!;

        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "only you", ["agent"] = addressed.Id });

        await BridgeHostFixture.WaitUntilAsync(() => addressed.RemarksIn(session).Count == 1, "the remark did not reach its addressee");
        Assert.Equal(0, other.RemarksIn(session).Count);
        await BridgeHostFixture.WaitUntilAsync(() => Described(plugin, addressed.Id)?["waiting"]?.GetValue<int>() == 1, "the panel was not told the remark waits");
    }

    [Fact]
    public async Task TakingBackOneAgentsRemarksLeavesTheOthersWaiting()
    {
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var first = await ClaimAsync(NewKey("s"), fileKey);
        var second = await ClaimAsync(NewKey("s"), fileKey);
        var session = bridge.Registry.Open(fileKey)!;
        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "both" });
        await BridgeHostFixture.WaitUntilAsync(() => second.RemarksIn(session).Count == 1, "the remark did not arrive");

        await plugin.SendEventAsync("REMARKS_CLEAR", new JsonObject { ["agent"] = first.Id });

        await BridgeHostFixture.WaitUntilAsync(() => first.RemarksIn(session).Count == 0, "the remark was not taken back");
        Assert.Equal(1, second.RemarksIn(session).Count);
    }

    [Fact]
    public async Task ACommandNamesTheAgentWhoseItIs()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var agent = await ClaimAsync(sessionId, fileKey);

        await MoveAsync(sessionId);

        var command = Assert.Single(plugin.Received, message => message["method"] is not null);
        Assert.Equal(agent.Id, command["agent"]!.GetValue<string>());
    }

    /// <summary>A write runs alone among the agent's commands: the next one waits for it, while another agent in the same file is not held up.</summary>
    [Fact]
    public async Task AnAgentsNextCommandWaitsForItsLastWhileAnotherAgentGoesAhead()
    {
        var fileKey = NewKey("f");
        var busy = NewKey("s");
        var free = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        await ClaimAsync(busy, fileKey);
        await ClaimAsync(free, fileKey);
        plugin.Silent.Add(EditorCommands.MoveNode.Name);

        var running = MoveAsync(busy);
        var queued = MoveAsync(busy);
        await BridgeHostFixture.WaitUntilAsync(() => Commands(plugin) == 1, "the first command was not sent");
        plugin.Silent.Clear();
        var other = MoveAsync(free);

        await other;
        Assert.Equal(2, Commands(plugin));
        Assert.False(queued.IsCompleted);

        await plugin.DisposeAsync();
        await Assert.ThrowsAsync<EditorUnavailableException>(() => running);
        await Assert.ThrowsAsync<EditorUnavailableException>(() => queued);
    }

    /// <summary>A dropped plugin fails its waits at once; before, they sat out the full timeout.</summary>
    [Fact]
    public async Task APluginThatDropsFailsItsWaitsAtOnce()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        await ClaimAsync(sessionId, fileKey);
        plugin.Silent.Add(EditorCommands.MoveNode.Name);
        var waiting = MoveAsync(sessionId);
        await BridgeHostFixture.WaitUntilAsync(() => Commands(plugin) == 1, "the command was not sent");
        var started = DateTimeOffset.UtcNow;

        await plugin.DisposeAsync();

        await Assert.ThrowsAsync<EditorUnavailableException>(() => waiting);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(3), "the wait ran into its timeout instead of failing with the connection");
    }

    [Fact]
    public async Task AnAgentThatLeavesGivesUpTheCommandsStillWaitingForTheirTurn()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        await ClaimAsync(sessionId, fileKey);
        plugin.Silent.Add(EditorCommands.MoveNode.Name);
        _ = MoveAsync(sessionId);
        var queued = MoveAsync(sessionId);
        await BridgeHostFixture.WaitUntilAsync(() => Commands(plugin) == 1, "the first command was not sent");

        await bridge.Roster.CloseAsync(sessionId, CancellationToken.None);

        var refusal = await Assert.ThrowsAsync<EditorUnavailableException>(() => queued);
        Assert.Contains("session ended", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(1, Commands(plugin));
    }

    private Task<JsonNode?> ReadSelectionAsync(string session)
    {
        using var serving = bridge.Roster.Serve(session, "claude-code");

        return bridge.Gateway.ReadAsync(EditorCommands.GetFileInfo);
    }

    /// <summary>An agent's reads overlap in the plugin, up to the lane's limit; reloading the plugin waits for them, since it ends every command in flight.</summary>
    [Fact]
    public async Task AnAgentsReadsRunTogetherAndAReloadWaitsForThem()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        await ClaimAsync(sessionId, fileKey);
        plugin.Silent.Add(EditorCommands.GetFileInfo.Name);

        var reads = Enumerable.Range(0, CommandLane.ReadsAtOnce + 1).Select(_ => ReadSelectionAsync(sessionId)).ToList();
        await BridgeHostFixture.WaitUntilAsync(() => Commands(plugin) == CommandLane.ReadsAtOnce, "the reads did not go out together");

        Task<JsonNode?> reload;

        using (bridge.Roster.Serve(sessionId, "claude-code"))
        {
            reload = bridge.Gateway.ReadAsync(EditorCommands.ReloadUi);
        }

        await Task.Delay(200);
        Assert.Equal(CommandLane.ReadsAtOnce, Commands(plugin));
        Assert.False(reload.IsCompleted);

        await plugin.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Task.WhenAll(reads));
        await Assert.ThrowsAsync<EditorUnavailableException>(() => reload);
    }
}
