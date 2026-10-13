using System.Collections.Concurrent;
using System.Net.WebSockets;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>One agent's view of the editor: the file its commands go to, the remarks left for it and where it stands in each console.</summary>
/// <remarks>The target moves only on the agent's word: a navigate, the only file there is, or the file the agent last named. Human clicks stamp the session and nothing else — with several agents open, a click in another file was meant for one of them, and the server cannot tell which. A target whose plugin went away is kept, not handed to another file: that file would be somebody else's.</remarks>
public sealed class AgentSlot : IOpenDocuments
{
    private readonly OpenDocumentRegistry _registry;

    private readonly AgentRoster _roster;

    private readonly ILogger _logger;

    /// <summary>Console marks by connection: a reconnected plugin brings a new console whose count starts from zero.</summary>
    private readonly ConcurrentDictionary<string, long> _consoleMarks = new(StringComparer.Ordinal);

    /// <summary>One command of this agent in flight per connection; the rest wait their turn here.</summary>
    private readonly ConcurrentDictionary<string, CommandLane> _lanes = new(StringComparer.Ordinal);

    private CancellationTokenSource _departure = new();

    private readonly Lock _naming = new();

    private Task? _named;

    private string? _targetFileKey;

    private string? _targetFileName;

    internal AgentSlot(string id, string name, OpenDocumentRegistry registry, AgentRoster roster, ILogger logger)
    {
        Id = id;
        Name = name;
        _registry = registry;
        _roster = roster;
        _logger = logger;
    }

    /// <summary>Stable within the process; the panel addresses the agent by it.</summary>
    public string Id { get; }

    /// <summary>What the human sees in the panel: the agent's project folder.</summary>
    public string Name { get; private set; }

    /// <summary>The file this agent last named in any tool, so <c>fileUrl</c> can be left out afterwards.</summary>
    public ActiveDocumentTracker Tracker { get; } = new();

    public IReadOnlyCollection<EditorSession> Sessions => _registry.Sessions;

    IReadOnlyCollection<IOpenDocument> IOpenDocuments.Sessions => Sessions;

    public bool HasConnection => _registry.HasConnection;

    public string? ActiveFileKey => _targetFileKey;

    /// <summary>The name of the target file, as its plugin last reported it.</summary>
    internal string? ActiveFileName => _targetFileName;

    public EditorSession? LastTouched => _registry.LastTouched;

    IOpenDocument? IOpenDocuments.LastTouched => LastTouched;

    /// <summary>Claims the file as the target and tells both panels: the new one that the agent is here, the old one whether anyone still is.</summary>
    public async Task<bool> TryClaimAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        if (_registry.Find(fileKey) is not { } session)
        {
            return false;
        }

        var previous = _targetFileKey;
        Claim(session);

        if (previous is not null && previous != fileKey && _registry.Open(previous) is { } left)
        {
            await _roster.AnnounceAsync(left, cancellationToken).ConfigureAwait(false);
        }

        if (session.IsOpen)
        {
            await _roster.AnnounceAsync(session, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Session for the file, or the target when no key is given; <c>null</c> when none is connected.</summary>
    /// <remarks>Without a target the only open file, or the file the agent last named in any tool, is adopted on the spot; several files with no such hint throw, because a guess would write into somebody else's file.</remarks>
    public EditorSession? Resolve(string? fileKey = null)
    {
        if (fileKey is not null)
        {
            return _registry.Open(fileKey);
        }

        if (_targetFileKey is { } target)
        {
            return _registry.Open(target);
        }

        var open = _registry.Sessions.Where(session => session.IsOpen).ToArray();

        if (open.Length == 0)
        {
            return null;
        }

        var chosen = open.Length == 1
            ? open[0]
            : open.FirstOrDefault(session => session.FileKey == Tracker.FileKey);

        if (chosen is null)
        {
            throw new EditorTargetAmbiguousException(Choices(open));
        }

        Claim(chosen);
        _ = AnnounceAsync(chosen);

        return chosen;
    }

    IOpenDocument? IOpenDocuments.Resolve(string? fileKey) => Resolve(fileKey);

    /// <summary>The remarks left in the document for this agent's project: whichever of its sessions reads first takes them.</summary>
    public Remarks RemarksIn(IOpenDocument document) =>
        _roster.RemarksOf(Address, document.FileKey ?? document.ConnectionId);

    /// <summary>Who else works in the document: this server's agents of other projects there, and the agents of other servers the panel names.</summary>
    /// <remarks>The panel names only the agents of its other connections; the agents sharing this server's connection are known here alone. Another session of this agent's own project is not somebody else.</remarks>
    public IReadOnlyList<string> OthersIn(IOpenDocument document)
    {
        var mine = _roster.WorkingIn(document.FileKey).Where(slot => slot.Address != Address).Select(slot => slot.ProjectName);

        return [.. mine.Concat(document.Peers).Distinct(StringComparer.Ordinal)];
    }

    public Task PublishRemarksWaitingAsync(IOpenDocument document, CancellationToken cancellationToken) =>
        _roster.PublishWaitingAsync(document, cancellationToken);

    public long ConsoleMark(IOpenDocument document) =>
        _consoleMarks.TryGetValue(document.ConnectionId, out var mark) ? mark : 0;

    public void ClearConsole(IOpenDocument document) =>
        _consoleMarks[document.ConnectionId] = document.ConsoleLogs.Added;

    /// <summary>Names the agent once: the first call starts <paramref name="naming"/>, and every call of the agent waits for that one answer.</summary>
    /// <remarks>A client often opens with several calls at once. Before, the first asked for the folder while the others went on to their tools: one of them chose a file before the agent knew its folder, and the choice was never written down to come back to.</remarks>
    internal Task NameOnceAsync(Func<Task> naming)
    {
        lock (_naming)
        {
            return _named ??= naming();
        }
    }

    /// <summary>The project folder the client works in, once it said so; the agent's file is kept by it across restarts and reconnects.</summary>
    /// <remarks>Not <see cref="Name"/>: a window that reconnects opens a new session while the old one has not yet timed out, and is named <c>Keynex 2</c> for that while — it is still Keynex, and its file is Keynex's.</remarks>
    internal string? Folder { get; private set; }

    /// <summary>How the panel addresses the agent: by its project, so two sessions of one project — a reconnect, a second conversation — are one agent there.</summary>
    /// <remarks>An agent that has not named its folder yet stands alone under its id.</remarks>
    internal string Address => Folder is { } folder ? $"{ProjectAddress}{folder}" : Id;

    /// <summary>What the panel shows: the project folder, without the number that tells its sessions apart in the log.</summary>
    internal string ProjectName => Folder ?? Name;

    private const string ProjectAddress = "project:";

    internal void NameAfterFolder(string folder, string name)
    {
        Folder = folder;
        Name = name;
    }

    /// <summary>Takes back the file the agent worked in before the server restarted; its panel is told with everyone else's.</summary>
    internal void Return(string fileKey, string? fileName)
    {
        _targetFileKey = fileKey;
        _targetFileName = fileName;
        Tracker.Remember(fileKey, fileName);
    }

    /// <summary>Cancelled when the agent leaves, so its commands still waiting for their turn give the turn up.</summary>
    internal CancellationToken Departure => _departure.Token;

    /// <summary>The turn of this agent's commands in the session's plugin: a few reads together, a write alone.</summary>
    internal CommandLane LaneTo(EditorSession session) =>
        _lanes.GetOrAdd(session.ConnectionId, _ => new CommandLane());

    /// <summary>How the agent's project stands in the file, as protocol 2 tells the panel: it works there when any of its sessions does.</summary>
    internal JsonObject DescribeIn(string? fileKey) => new()
    {
        ["id"] = Address,
        ["name"] = ProjectName,
        ["bound"] = _roster.WorkingIn(fileKey).Any(slot => slot.Address == Address),
        ["waiting"] = fileKey is null ? 0 : WaitingIn(fileKey),
    };

    /// <summary>How many remarks wait for this agent's project in the file.</summary>
    internal int WaitingIn(string fileKey) => _roster.WaitingFor(Address, fileKey);

    /// <summary>Why a command found no plugin: the file this agent works in by name when it has one, so the agent knows what to ask the human for.</summary>
    internal EditorUnavailableException Unavailable(string? fileKey)
    {
        if (fileKey is null && _targetFileKey is { } target)
        {
            return new EditorUnavailableException(
                $"The file this agent works in, {_targetFileName ?? "unnamed"} ({target}), is not connected. " +
                "Run the plugin there (Plugins → Development → Snail Bridge), or call figma_navigate with another file.");
        }

        return new EditorUnavailableException(
            "The Snail Bridge plugin is not connected. Open the file in Figma Desktop and run the plugin " +
            "(Plugins → Development → Snail Bridge).");
    }

    /// <summary>Takes a newly connected file as the target when it is the only file there is and the agent has none yet.</summary>
    internal void AdoptIfAlone(EditorSession session)
    {
        if (_targetFileKey is null && _registry.Sessions.Count == 1)
        {
            Claim(session);
        }
    }

    /// <summary>Forgets the target and the file last named, as for an agent that has just arrived: its session ended, or a test needs one that has not chosen yet.</summary>
    internal void Release()
    {
        _targetFileKey = null;
        _targetFileName = null;
        Tracker.Forget();

        var departed = Interlocked.Exchange(ref _departure, new CancellationTokenSource());
        departed.Cancel();
        departed.Dispose();
    }

    private void Claim(EditorSession session)
    {
        if (_targetFileKey == session.FileKey)
        {
            return;
        }

        _targetFileKey = session.FileKey;
        _targetFileName = session.FileName;
        Tracker.Remember(session.FileKey!, session.FileName);
        Log.TargetChosen(_logger, session.FileName, session.FileKey);
        _roster.Remember(this);
    }

    /// <summary>Tells the panel of a file adopted inside a synchronous resolve; a lost notice only delays the panel until the first command arrives.</summary>
    private async Task AnnounceAsync(EditorSession session)
    {
        try
        {
            await _roster.AnnounceAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            Log.PanelNotTold(_logger, ex, session.FileKey);
        }
    }

    private static string Choices(IEnumerable<EditorSession> open) =>
        $"Several files are connected and none is the target: {string.Join(", ", open.Select(session => $"{session.FileName ?? "unnamed"} ({session.FileKey})"))}.";
}
