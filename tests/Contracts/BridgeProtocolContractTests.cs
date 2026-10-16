using System.Text.RegularExpressions;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Contracts;

/// <summary>The bridge protocol as both ends write it: every message the server sends is handled by <c>plugin/ui.html</c>, every event the server reads is sent by it, and protocol 2 names the agent where the server looks for it.</summary>
/// <remarks>The fake plugin in the other tests speaks the protocol as the tests imagine it; only the bundle itself says what the real panel does. A message one side renamed fails here instead of going silent in Figma.</remarks>
public partial class BridgeProtocolContractTests
{
    private static readonly string Panel = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "plugin", "ui.html"));

    /// <summary>Every message the server can push, by the type it carries.</summary>
    private static IReadOnlyList<string> ServerMessages() =>
    [
        Type(EditorWireProtocol.ServerHello(9223, "0.0.0", "agent")),
        Type(EditorWireProtocol.TargetBound(true)),
        Type(EditorWireProtocol.Agents([])),
        Type(EditorWireProtocol.RemarksWaiting(0)),
        Type(EditorWireProtocol.BuildProgress(0, 0)),
        Type(EditorWireProtocol.PluginUpdateAvailable()),
        Type(EditorWireProtocol.PortsAlive([])),
    ];

    [Fact]
    public void BridgeProtocol_MessageTheServerSends_IsHandledByThePanel()
    {
        var handled = HandledPattern.Matches(Panel).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var unhandled = ServerMessages().Where(type => !handled.Contains(type)).ToList();

        Assert.True(unhandled.Count == 0, $"plugin/ui.html does not handle: {string.Join(", ", unhandled)}.");
    }

    [Fact]
    public void BridgeProtocol_EventTheServerReads_IsSentByThePanel()
    {
        var sent = SentPattern.Matches(Panel).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var read = typeof(EditorWireProtocol.Events)
            .GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        var unsent = read.Where(type => !sent.Contains(type)).ToList();

        Assert.True(unsent.Count == 0, $"the server reads events plugin/ui.html never sends: {string.Join(", ", unsent)}.");
    }

    /// <summary>The panel says which protocol it speaks in FILE_INFO, and it is the one the server names agents in.</summary>
    [Fact]
    public void BridgeProtocol_PanelSendsFileInfo_DeclaresTheAgentsProtocol()
    {
        var spoken = ProtocolPattern.Match(Panel);

        Assert.True(spoken.Success, "plugin/ui.html does not tell the server its protocol in FILE_INFO.");
        Assert.Equal(EditorWireProtocol.AgentsProtocol, int.Parse(spoken.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A remark and taking remarks back name their agent; sent bare, they would reach every agent of a shared server.</summary>
    [Fact]
    public void BridgeProtocol_RemarksSentOrCleared_NameTheirAgent()
    {
        Assert.Contains("sendToAgents({ type: 'BUILD_STOP'", Panel, StringComparison.Ordinal);
        Assert.Contains("addressed({ type: 'REMARKS_CLEAR' }", Panel, StringComparison.Ordinal);
        Assert.Contains("data.agent = agent.id;", Panel, StringComparison.Ordinal);
    }

    /// <summary>The panel reads every field the server describes an agent with.</summary>
    [Fact]
    public void BridgeProtocol_AgentsRoster_EveryFieldIsReadByThePanel()
    {
        var described = new AgentRosterProbe().Fields();

        var unread = described.Where(field => !Panel.Contains($"entry.{field}", StringComparison.Ordinal)).ToList();

        Assert.True(unread.Count == 0, $"plugin/ui.html ignores these fields of AGENTS: {string.Join(", ", unread)}.");
    }

    private static string Type(JsonObject message) => message["type"]!.GetValue<string>();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "plugin")))
            {
                return directory.FullName;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("the plugin folder was not found above the test binaries");
    }

    [GeneratedRegex(@"message\.type === '([A-Z_]+)'")]
    private static partial Regex HandledPattern { get; }

    [GeneratedRegex(@"type: '([A-Z_]+)'")]
    private static partial Regex SentPattern { get; }

    [GeneratedRegex(@"introduced\.protocol = (\d+);")]
    private static partial Regex ProtocolPattern { get; }

    /// <summary>The fields of an agent as the roster describes it, read from the description itself rather than restated here.</summary>
    private sealed class AgentRosterProbe
    {
        public IReadOnlyList<string> Fields()
        {
            var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
            var roster = new AgentRoster(registry, new ServerConfig(), NullLogger<AgentRoster>.Instance);

            return [.. roster.Default.DescribeIn("file").Select(pair => pair.Key)];
        }
    }
}
