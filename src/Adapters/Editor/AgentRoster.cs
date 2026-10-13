using System.Net.WebSockets;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>The agents this server works for, which of them the call in hand belongs to, and what each file's panel is told about them.</summary>
/// <remarks>Over stdio there is one agent, the one that started the process, named after its folder. A shared server opens an agent for every MCP session and closes it when the session ends; a call is served as its session's agent for as long as it runs, so the tools built for it and the commands they send are that agent's. The panel learns per file whether any agent of this server works there, and a remark typed in that file goes to those agents and nobody else.</remarks>
public sealed class AgentRoster
{
    private readonly List<AgentSlot> _slots = [];

    private readonly Dictionary<string, AgentSlot> _bySession = new(StringComparer.Ordinal);

    private readonly AsyncLocal<AgentSlot?> _serving = new();

    private readonly object _gate = new();

    private readonly OpenDocumentRegistry _registry;

    private readonly ILogger<AgentRoster> _logger;

    private readonly bool _isShared;

    private readonly AgentTargets? _targets;

    private int _issued = 1;

    public AgentRoster(OpenDocumentRegistry registry, ServerConfig config, ILogger<AgentRoster> logger)
    {
        _registry = registry;
        _logger = logger;
        _isShared = config.IsShared;
        _targets = _isShared ? new AgentTargets(Path.Combine(config.DataDirectory, AgentsFileName), logger) : null;
        Default = new AgentSlot("a1", config.Bridge.AgentName, registry, this, logger);

        if (!_isShared)
        {
            _slots.Add(Default);
        }
    }

    /// <summary>Where a shared server keeps its agents' files across restarts, in the data folder.</summary>
    public const string AgentsFileName = "agents.json";

    /// <summary>The agent of a call made outside any session: over stdio the one that started the process; on a shared server nobody, since it never takes a file.</summary>
    public AgentSlot Default { get; }

    /// <summary>Whether this server is shared by every agent of the machine rather than started by one.</summary>
    public bool IsShared => _isShared;

    /// <summary>The agent the call in hand belongs to.</summary>
    public AgentSlot Current => _serving.Value ?? Default;

    /// <summary>Every agent working through this server now.</summary>
    public IReadOnlyList<AgentSlot> Slots
    {
        get { lock (_gate) { return [.. _slots]; } }
    }

    /// <summary>Serves the call in hand as the agent of its MCP session until the returned handle is disposed.</summary>
    /// <remarks>Over stdio every call is the one agent's. On a shared server a session's first call opens its agent; a client of the sessionless protocol revision holds no session and is recognised by the name it gives instead.</remarks>
    public IDisposable Serve(string? sessionId, string? clientName)
    {
        _serving.Value = _isShared ? Open(sessionId ?? $"client:{clientName}", clientName) : Default;

        return new Served(_serving);
    }

    /// <summary>The agent of the session, opened on first sight; every panel learns that it arrived.</summary>
    internal AgentSlot Open(string sessionId, string? clientName)
    {
        AgentSlot opened;

        lock (_gate)
        {
            if (_bySession.TryGetValue(sessionId, out var known))
            {
                return known;
            }

            _issued++;
            opened = new AgentSlot($"a{_issued}", UniqueName(Called(clientName) ?? $"agent {_issued}", null), _registry, this, _logger);
            _bySession[sessionId] = opened;
            _slots.Add(opened);
        }

        Log.AgentArrived(_logger, opened.Name, opened.Id);
        _ = AnnounceEverywhereAsync();

        return opened;
    }

    /// <summary>Names the agent after its project, once the client has said which folder it works in, and gives it back the file it worked in before a restart; every panel learns both.</summary>
    public async Task NameAsync(AgentSlot agent, string name)
    {
        lock (_gate)
        {
            agent.NameAfterFolder(UniqueName(name, agent));
        }

        Log.AgentNamed(_logger, agent.Id, agent.Name);

        if (agent.ActiveFileKey is null &&
            _targets?.Recall(agent.Name, DateTimeOffset.UtcNow, fileKey => _registry.Open(fileKey) is not null) is var (fileKey, fileName))
        {
            agent.Return(fileKey, fileName);
            Log.AgentReturned(_logger, agent.Name, fileName, fileKey);
        }

        await AnnounceEverywhereAsync().ConfigureAwait(false);
    }

    /// <summary>Writes down where an agent named after its folder works now, so it gets the file back after a restart.</summary>
    internal void Remember(AgentSlot agent)
    {
        if (agent.IsNamedByFolder)
        {
            _targets?.Remember(agent.Name, agent.ActiveFileKey, agent.ActiveFileName, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>Writes down every agent's file as the server stops, so the records a restart reads are minutes old rather than as old as each agent's last navigate.</summary>
    public void RememberAll()
    {
        foreach (var agent in Slots)
        {
            Remember(agent);
        }
    }

    /// <summary>The session ended: its agent leaves, its commands still waiting for their turn give it up, and every panel learns who is left.</summary>
    public async Task CloseAsync(string sessionId, CancellationToken cancellationToken)
    {
        AgentSlot? leaving;

        lock (_gate)
        {
            if (!_bySession.Remove(sessionId, out leaving))
            {
                return;
            }

            _slots.Remove(leaving);
        }

        leaving.Release();
        Log.AgentLeft(_logger, leaving.Name, leaving.Id);

        await AnnounceEverywhereAsync().ConfigureAwait(false);
    }

    /// <summary>The agents whose target is the file.</summary>
    public IReadOnlyList<AgentSlot> WorkingIn(string? fileKey) =>
        fileKey is null ? [] : [.. Slots.Where(slot => slot.ActiveFileKey == fileKey)];

    /// <summary>How many remarks wait in the file for any agent of this server.</summary>
    public int WaitingIn(string? fileKey) =>
        fileKey is null ? 0 : Slots.Sum(slot => slot.WaitingIn(fileKey));

    /// <summary>A plugin identified its file: an agent with no target takes the only file there is, and a panel that reloaded learns again who works in it.</summary>
    public async Task OnConnectedAsync(EditorSession session, CancellationToken cancellationToken)
    {
        foreach (var slot in Slots)
        {
            slot.AdoptIfAlone(session);
        }

        if (session.NamesAgents || WorkingIn(session.FileKey).Count > 0)
        {
            await AnnounceAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Tells the panel who works in its file: every agent by name to a panel of protocol 2, whether anyone does to an older one.</summary>
    public Task AnnounceAsync(IOpenDocument session, CancellationToken cancellationToken) =>
        session is EditorSession { NamesAgents: true } named
            ? named.SendAsync(EditorWireProtocol.Agents(new JsonArray([.. Slots.Select(slot => (JsonNode)slot.DescribeIn(named.FileKey))])), cancellationToken)
            : session.PublishTargetBoundAsync(WorkingIn(session.FileKey).Count > 0, cancellationToken);

    /// <summary>Tells the panel how many remarks still wait there: per agent to a panel of protocol 2, all of them together to an older one.</summary>
    public Task PublishWaitingAsync(IOpenDocument session, CancellationToken cancellationToken) =>
        session is EditorSession { NamesAgents: true }
            ? AnnounceAsync(session, cancellationToken)
            : session.PublishRemarksWaitingAsync(WaitingIn(session.FileKey), cancellationToken);

    /// <summary>Queues the human's remark for the agent it names, or for every agent working in the file when it names none, and only if they work there.</summary>
    /// <remarks>Every server is connected to every panel. A remark for another server's agent, queued here, would light this panel's queue for a reader that never comes and stop a build the human was not looking at.</remarks>
    public async Task QueueRemarkAsync(EditorSession session, BuildInterruption remark, string? agent, CancellationToken cancellationToken)
    {
        var working = WorkingIn(session.FileKey).Where(slot => agent is null || slot.Id == agent).ToList();

        if (working.Count == 0)
        {
            Log.RemarkForAnotherAgent(_logger, session.FileKey, string.Join(", ", Slots.Select(slot => slot.ActiveFileKey ?? "no file yet")));

            return;
        }

        foreach (var slot in working)
        {
            slot.RemarksIn(session).Add(remark);
        }

        Log.HumanSteppedIn(_logger, session.FileKey);
        await PublishWaitingAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The human took the remarks waiting for the agent back before it read them, or every agent's when the panel names none.</summary>
    public Task ClearRemarksAsync(EditorSession session, string? agent, CancellationToken cancellationToken)
    {
        foreach (var slot in Slots.Where(slot => agent is null || slot.Id == agent))
        {
            slot.RemarksIn(session).TakeAll();
        }

        return PublishWaitingAsync(session, cancellationToken);
    }

    /// <summary>Tells every open panel who is here now: an agent arrived, left, or changed its name.</summary>
    /// <remarks>A notice that cannot be delivered is dropped: the panel it was for is closing, and it will be told again when it reconnects.</remarks>
    internal async Task AnnounceEverywhereAsync()
    {
        foreach (var session in _registry.Sessions.Where(session => session.IsOpen))
        {
            try
            {
                await AnnounceAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                Log.PanelNotTold(_logger, ex, session.FileKey);
            }
        }
    }

    /// <summary>Two sessions from one project are told apart in the panel as <c>Keynex</c> and <c>Keynex 2</c>.</summary>
    private string UniqueName(string name, AgentSlot? self)
    {
        var taken = _slots.Where(slot => slot != self).Select(slot => slot.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = name;

        for (var number = 2; taken.Contains(unique); number++)
        {
            unique = $"{name} {number}";
        }

        return unique;
    }

    private static string? Called(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>Ends the serving of a call: what runs after it on this flow belongs to no session.</summary>
    private sealed class Served(AsyncLocal<AgentSlot?> serving) : IDisposable
    {
        public void Dispose() => serving.Value = null;
    }
}
