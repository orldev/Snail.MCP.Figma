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
    public async Task AgentRoster_PanelSpeaksProtocolTwo_IsToldEveryAgentAndWhereEachWorks()
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
    public async Task EditorSocketHub_RemarkAddressedToOneOfTwoAgents_ReachesOnlyIt()
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
    public async Task EditorSocketHub_OneAgentsRemarksTakenBack_OthersStayWaiting()
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
    public async Task LiveEditorGateway_CommandSent_NamesTheAgentWhoseItIs()
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
    public async Task LiveEditorGateway_AgentsCommandStillRunning_NextWaitsWhileAnotherAgentGoesAhead()
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
    public async Task LiveEditorGateway_PluginDrops_FailsItsWaitsAtOnce()
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
    public async Task LiveEditorGateway_AgentLeaves_GivesUpItsQueuedCommands()
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

    /// <summary>Reads take turns too: a read's timeout runs from the moment it is sent, and must not be spent waiting in the plugin behind the agent's own earlier reads.</summary>
    [Fact]
    public async Task LiveEditorGateway_AgentSendsSeveralReads_TheyGoToThePluginOneAtATime()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        await ClaimAsync(sessionId, fileKey);
        plugin.Silent.Add(EditorCommands.GetFileInfo.Name);

        var first = ReadSelectionAsync(sessionId);
        var second = ReadSelectionAsync(sessionId);
        await BridgeHostFixture.WaitUntilAsync(() => Commands(plugin) == 1, "the first read was not sent");

        await Task.Delay(200);
        Assert.Equal(1, Commands(plugin));
        Assert.False(second.IsCompleted);

        await plugin.DisposeAsync();
        await Assert.ThrowsAsync<EditorUnavailableException>(() => first);
        await Assert.ThrowsAsync<EditorUnavailableException>(() => second);
    }

    private static int Entries(FakeEditorExtension plugin, string name) => plugin.Received
        .LastOrDefault(message => message["type"]?.GetValue<string>() == "AGENTS")?["data"]?["agents"]?.AsArray()
        .Count(entry => entry?["name"]?.GetValue<string>() == name) ?? 0;

    /// <summary>A second conversation of one project, or its reconnect before the old session timed out, is still the one project in the panel: one entry, one queue of remarks, and not somebody else to itself.</summary>
    [Fact]
    public async Task AgentRoster_TwoSessionsOfOneProject_AreOneAgentInThePanel()
    {
        var fileKey = NewKey("f");
        var project = NewKey("p");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var first = await ClaimAsync(NewKey("s"), fileKey);
        var second = await ClaimAsync(NewKey("s"), fileKey);
        await bridge.Roster.NameAsync(first, project);
        await bridge.Roster.NameAsync(second, project);
        var session = bridge.Registry.Open(fileKey)!;

        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "for the project", ["agent"] = first.Address });

        await BridgeHostFixture.WaitUntilAsync(() => second.RemarksIn(session).Count == 1, "the remark did not reach the project");
        Assert.Equal(1, Entries(plugin, project));
        Assert.Equal(0, Entries(plugin, $"{project} 2"));
        Assert.Equal(1, Described(plugin, first.Address)!["waiting"]!.GetValue<int>());
        Assert.Single(first.RemarksIn(session).TakeAll());
        Assert.Equal(0, second.RemarksIn(session).Count);
        Assert.DoesNotContain(project, first.OthersIn(session));
    }

    /// <summary>When one of the project's sessions ends, the other still reads what was left for the project.</summary>
    [Fact]
    public async Task AgentRoster_OneProjectSessionEnds_RemarkForTheProjectSurvives()
    {
        var fileKey = NewKey("f");
        var project = NewKey("p");
        var leaving = NewKey("s");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var gone = await ClaimAsync(leaving, fileKey);
        var staying = await ClaimAsync(NewKey("s"), fileKey);
        await bridge.Roster.NameAsync(gone, project);
        await bridge.Roster.NameAsync(staying, project);
        var session = bridge.Registry.Open(fileKey)!;
        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "still here" });
        await BridgeHostFixture.WaitUntilAsync(() => staying.RemarksIn(session).Count == 1, "the remark did not arrive");

        await bridge.Roster.CloseAsync(leaving, CancellationToken.None);

        Assert.Equal(1, staying.RemarksIn(session).Count);
    }

    [Fact]
    public async Task LiveEditorGateway_AgentNamedAfterAProject_CommandNamesTheProject()
    {
        var fileKey = NewKey("f");
        var sessionId = NewKey("s");
        var project = NewKey("p");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey, protocol: Protocol);
        var agent = await ClaimAsync(sessionId, fileKey);
        await bridge.Roster.NameAsync(agent, project);

        await MoveAsync(sessionId);

        var command = Assert.Single(plugin.Received, message => message["method"] is not null);
        Assert.Equal(agent.Address, command["agent"]!.GetValue<string>());
        Assert.Equal(project, Described(plugin, agent.Address)!["name"]!.GetValue<string>());
    }
}
