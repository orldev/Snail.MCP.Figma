using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>One server, several agents: each MCP session is an agent of its own, and nothing one of them chose or was told reaches another.</summary>
/// <remarks>Its own host in the shared mode; every test opens sessions of its own, so the agents of one test never meet those of another.</remarks>
public class SharedAgentsTests(SharedBridgeHostFixture bridge) : IClassFixture<SharedBridgeHostFixture>
{
    private static string NewKey(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..10]}";

    private static bool? LastBound(FakeEditorExtension plugin) => plugin.Received
        .LastOrDefault(message => message["type"]?.GetValue<string>() == "TARGET_BOUND")?["data"]?["bound"]?.GetValue<bool>();

    private static int Commands(FakeEditorExtension plugin) =>
        plugin.Received.Count(message => message["method"] is not null);

    private Task NoFilesAsync() => BridgeHostFixture.WaitUntilAsync(
        () => bridge.Registry.Sessions.Count == 0,
        "the previous test's sessions did not leave the registry");

    /// <summary>Claims the file as the target of the session's agent, the way figma_navigate does inside its call.</summary>
    private async Task ClaimAsync(string session, string fileKey)
    {
        using var serving = bridge.Roster.Serve(session, "claude-code");
        Assert.True(await bridge.Gateway.Documents.TryClaimAsync(fileKey));
    }

    private async Task MoveAsync(string session)
    {
        using var serving = bridge.Roster.Serve(session, "claude-code");
        await bridge.Gateway.ReadAsync(EditorCommands.MoveNode, new JsonObject { ["nodeId"] = "1:1" });
    }

    [Fact]
    public async Task LiveEditorGateway_TwoAgentsInTwoFiles_CommandGoesToTheCallersFile()
    {
        var first = NewKey("s");
        var second = NewKey("s");
        var web = NewKey("f");
        var mobile = NewKey("f");
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await ClaimAsync(first, web);
        await ClaimAsync(second, mobile);

        await MoveAsync(first);
        await MoveAsync(first);
        await MoveAsync(second);

        Assert.Equal(2, Commands(pluginWeb));
        Assert.Equal(1, Commands(pluginMobile));
    }

    /// <summary>A shared server is nobody's agent: if it took the only file by itself, the panel would show an agent that is not there.</summary>
    [Fact]
    public async Task AgentRoster_SharedServerSeesOneFile_DefaultAgentTakesNoFile()
    {
        await NoFilesAsync();
        var fileKey = NewKey("f");

        await using var plugin = await bridge.ConnectPluginAsync(fileKey);

        Assert.Null(bridge.Roster.Default.ActiveFileKey);
        Assert.Empty(bridge.Roster.WorkingIn(fileKey));
        Assert.NotEqual(true, LastBound(plugin));
    }

    [Fact]
    public async Task EditorSocketHub_RemarkTypedInAFile_ReachesOnlyTheAgentsWorkingThere()
    {
        var mine = NewKey("s");
        var theirs = NewKey("s");
        var web = NewKey("f");
        var mobile = NewKey("f");
        await using var pluginWeb = await bridge.ConnectPluginAsync(web, "Web");
        await using var pluginMobile = await bridge.ConnectPluginAsync(mobile, "Mobile");
        await ClaimAsync(mine, web);
        await ClaimAsync(theirs, mobile);
        var me = bridge.Roster.Open(mine, null);
        var them = bridge.Roster.Open(theirs, null);
        var webSession = bridge.Registry.Open(web)!;

        await pluginWeb.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "for the web agent" });

        await BridgeHostFixture.WaitUntilAsync(() => me.RemarksIn(webSession).Count == 1, "the remark did not reach its agent");
        Assert.Equal(0, them.RemarksIn(webSession).Count);
        Assert.Equal(0, them.RemarksIn(bridge.Registry.Open(mobile)!).Count);
    }

    /// <summary>The panel of an older plugin cannot name the addressee, so both agents in its file hear it, as two servers did before.</summary>
    [Fact]
    public async Task EditorSocketHub_RemarkNamesNobody_BothAgentsInTheFileHearIt()
    {
        var first = NewKey("s");
        var second = NewKey("s");
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        await ClaimAsync(first, fileKey);
        await ClaimAsync(second, fileKey);
        var session = bridge.Registry.Open(fileKey)!;

        await plugin.SendEventAsync("BUILD_STOP", new JsonObject { ["remark"] = "both of you" });

        await BridgeHostFixture.WaitUntilAsync(
            () => bridge.Roster.Open(first, null).RemarksIn(session).Count == 1 && bridge.Roster.Open(second, null).RemarksIn(session).Count == 1,
            "the remark did not reach both agents");
    }

    [Fact]
    public async Task AgentRoster_SessionEnds_AgentLeavesAndThePanelLearnsIt()
    {
        var leaving = NewKey("s");
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        await ClaimAsync(leaving, fileKey);
        await BridgeHostFixture.WaitUntilAsync(() => LastBound(plugin) == true, "the panel was not told the agent is here");
        var agent = bridge.Roster.Open(leaving, null);

        await bridge.Roster.CloseAsync(leaving, CancellationToken.None);

        await BridgeHostFixture.WaitUntilAsync(() => LastBound(plugin) == false, "the panel was not told the agent left");
        Assert.DoesNotContain(agent, bridge.Roster.Slots);
        Assert.Empty(bridge.Roster.WorkingIn(fileKey));
    }

    [Fact]
    public async Task AgentRoster_SessionsOpenedUnderOneProject_AreToldApartAndANamelessOneIsNumbered()
    {
        var project = NewKey("p");

        var first = bridge.Roster.Open(NewKey("s"), project);
        var second = bridge.Roster.Open(NewKey("s"), project);
        var nameless = bridge.Roster.Open(NewKey("s"), " ");

        Assert.Equal(project, first.Name);
        Assert.Equal($"{project} 2", second.Name);
        Assert.StartsWith("agent ", nameless.Name, StringComparison.Ordinal);
        Assert.NotEqual(first.Id, second.Id);
    }

    /// <summary>Clearing is one agent's: the other agent in the file still reads every line.</summary>
    [Fact]
    public async Task AgentSlot_OneAgentClearsTheConsole_OtherStillReadsIt()
    {
        var clearing = NewKey("s");
        var reading = NewKey("s");
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        var session = bridge.Registry.Open(fileKey)!;
        await plugin.SendEventAsync("CONSOLE_CAPTURE", new JsonObject { ["level"] = "log", ["message"] = "built" });
        await BridgeHostFixture.WaitUntilAsync(() => session.ConsoleLogs.Count == 1, "the console line did not arrive");

        bridge.Roster.Open(clearing, null).ClearConsole(session);

        Assert.Empty(session.ConsoleLogs.Since(bridge.Roster.Open(clearing, null).ConsoleMark(session)));
        Assert.Single(session.ConsoleLogs.Since(bridge.Roster.Open(reading, null).ConsoleMark(session)));
    }

    /// <summary>The folder arrives after the agent opened under its client's name; two windows on one project are still told apart.</summary>
    [Fact]
    public async Task AgentRoster_AgentsNamedAfterOneFolder_AreToldApart()
    {
        var project = NewKey("p");
        var first = bridge.Roster.Open(NewKey("s"), "claude-code");
        var second = bridge.Roster.Open(NewKey("s"), "claude-code");

        await bridge.Roster.NameAsync(first, project);
        await bridge.Roster.NameAsync(second, project);
        await bridge.Roster.NameAsync(first, project);

        Assert.Equal(project, first.Name);
        Assert.Equal($"{project} 2", second.Name);
    }

    /// <summary>One file's connection carries every agent working there: an event the hub cannot read is skipped, and the file stays connected for all of them.</summary>
    [Fact]
    public async Task EditorSocketHub_UnreadableEvent_IsSkippedAndTheFileStaysConnected()
    {
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        var session = bridge.Registry.Open(fileKey)!;

        await plugin.SendEventAsync("PAGE_CHANGE", new JsonObject { ["pageName"] = 42 });
        await plugin.SendEventAsync("SELECTION_CHANGE", new JsonObject { ["count"] = 1, ["nodes"] = new JsonArray(new JsonObject { ["id"] = "1:2" }) });

        await BridgeHostFixture.WaitUntilAsync(() => session.Selection is not null, "the connection did not survive the unreadable event");
        Assert.True(session.IsOpen);
        Assert.Same(session, bridge.Registry.Open(fileKey));
    }

    /// <summary>The panel names only the agents of its other connections; two agents sharing this server's connection learn of each other from the server.</summary>
    [Fact]
    public async Task AgentSlot_TwoAgentsOfThisServerInOneFile_AreToldOfEachOther()
    {
        var fileKey = NewKey("f");
        await using var plugin = await bridge.ConnectPluginAsync(fileKey);
        var first = NewKey("s");
        var second = NewKey("s");
        await ClaimAsync(first, fileKey);
        await ClaimAsync(second, fileKey);
        var firstAgent = bridge.Roster.Open(first, null);
        var secondAgent = bridge.Roster.Open(second, null);
        var session = bridge.Registry.Open(fileKey)!;

        Assert.Contains(secondAgent.Name, firstAgent.OthersIn(session));
        Assert.Contains(firstAgent.Name, secondAgent.OthersIn(session));
        Assert.DoesNotContain(firstAgent.Name, firstAgent.OthersIn(session));
    }
}
