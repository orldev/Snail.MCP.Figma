using System.Net.WebSockets;
using System.Text;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>One plugin connection — one file open in Figma.</summary>
public sealed class EditorSession : IOpenDocument
{
    /// <summary>Sends are serialized: WebSocket forbids concurrent SendAsync calls.</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private readonly WebSocket _socket;

    /// <summary>Wraps a freshly accepted plugin socket, unidentified until its <c>FILE_INFO</c> arrives.</summary>
    public EditorSession(WebSocket socket, string connectionId)
    {
        _socket = socket;
        ConnectionId = connectionId;
        ConnectedAt = DateTimeOffset.UtcNow;
        LastActivityAt = ConnectedAt;
        LastInteractionAt = DateTimeOffset.MinValue;
    }

    /// <summary>The hub's id for this socket, unique for as long as the process runs.</summary>
    public string ConnectionId { get; }

    /// <summary>Filled from <c>FILE_INFO</c>; until then the session is unidentified.</summary>
    public string? FileKey { get; set; }

    /// <summary>The file's name as the plugin reported it in <c>FILE_INFO</c>.</summary>
    public string? FileName { get; set; }

    /// <summary>Name of the page the human has open, updated on every page change.</summary>
    public string? CurrentPage { get; set; }

    /// <summary>Id of the page the human has open, known once the human switches a page.</summary>
    public string? CurrentPageId { get; set; }

    /// <summary>Version of the plugin code running in the file, compared with the installed bundle.</summary>
    public string? PluginVersion { get; set; }

    /// <summary>Which Figma editor the file is open in, as reported by the plugin.</summary>
    public string? EditorType { get; set; }

    /// <summary>The bridge protocol the panel speaks, from <c>FILE_INFO</c>: 1 knows one agent per connection, 2 names each agent in its messages.</summary>
    public int Protocol { get; set; } = 1;

    /// <summary>Whether the panel tells agents apart within this connection (<see cref="EditorWireProtocol.Agents"/>).</summary>
    public bool NamesAgents => Protocol >= EditorWireProtocol.AgentsProtocol;

    /// <summary>When the plugin's socket was accepted.</summary>
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>When a message last went either way between the server and this plugin.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>When the human last selected, switched a page or wrote a remark; the minimum until they do.</summary>
    public DateTimeOffset LastInteractionAt { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<string> Peers { get; set; } = [];

    /// <summary>Last known selection — the source of coordinates for mutations.</summary>
    public JsonNode? Selection { get; set; }

    /// <summary>How long a variables snapshot is believed without asking the plugin again.</summary>
    /// <remarks>Figma's documentchange event says nothing about variables, so an edit a human makes in the variables panel reaches the server as silence; age is the only signal left for it.</remarks>
    public static readonly TimeSpan VariablesBelievedFor = TimeSpan.FromSeconds(30);

    private readonly Lock _variablesGate = new();

    private DateTimeOffset _variablesTakenAt;

    private long _writes;

    private long _writesInSnapshot;

    /// <summary>Variables as the plugin last sent them; served without the socket only while <see cref="HasFreshVariables"/>.</summary>
    public JsonNode? VariablesSnapshot { get; private set; }

    /// <summary>Whether the snapshot may answer a read: it exists, it already contains every write made so far, and it is young.</summary>
    public bool HasFreshVariables => HasFreshVariablesAt(DateTimeOffset.UtcNow);

    internal bool HasFreshVariablesAt(DateTimeOffset moment)
    {
        lock (_variablesGate)
        {
            return VariablesSnapshot is not null && _writesInSnapshot == _writes && moment - _variablesTakenAt < VariablesBelievedFor;
        }
    }

    /// <summary>How many writes the file has seen so far; a reader takes it before asking the plugin, and the answer contains exactly those.</summary>
    public long WritesSoFar
    {
        get { lock (_variablesGate) { return _writes; } }
    }

    /// <summary>Takes variables the plugin read when asked after <paramref name="writesWhenAsked"/> writes: believed while no write came since.</summary>
    public void RememberVariables(JsonNode? variables, long writesWhenAsked)
    {
        lock (_variablesGate)
        {
            VariablesSnapshot = variables;
            _variablesTakenAt = DateTimeOffset.UtcNow;
            _writesInSnapshot = writesWhenAsked;
        }
    }

    /// <summary>Takes variables the panel pushed on its own, read at a moment nobody knows: believed only while no write came since the last snapshot that was.</summary>
    /// <remarks>The panel broadcasts every REFRESH result to all servers. One that another server asked for before this server's write can arrive after it, and believed, it would hide the agent's new variable for thirty seconds.</remarks>
    public void RememberVariables(JsonNode? variables)
    {
        lock (_variablesGate)
        {
            VariablesSnapshot = variables;
            _variablesTakenAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Something was written to the file, by the agent or by the human, so the snapshot may no longer match it.</summary>
    public void DoubtVariables()
    {
        lock (_variablesGate)
        {
            _writes++;
        }
    }

    /// <summary>Ring buffer of the plugin sandbox console.</summary>
    public RingBuffer<JsonNode> ConsoleLogs { get; } = new(1000);

    /// <summary>Ring buffer of document-change events.</summary>
    public RingBuffer<JsonNode> DocumentChanges { get; } = new(200);

    /// <summary>Ring buffer of description and annotation edits, which REST version snapshots never expose.</summary>
    public RingBuffer<JsonNode> MetadataChanges { get; } = new(200);

    /// <summary>Tells the panel how many remarks are still waiting for the agents working in this file.</summary>
    public Task PublishRemarksWaitingAsync(int waiting, CancellationToken cancellationToken) =>
        SendAsync(EditorWireProtocol.RemarksWaiting(waiting), cancellationToken);

    /// <inheritdoc/>
    public Task PublishBuildProgressAsync(int step, int total, CancellationToken cancellationToken) =>
        SendAsync(EditorWireProtocol.BuildProgress(step, total), cancellationToken);

    /// <inheritdoc/>
    public Task PublishTargetBoundAsync(bool bound, CancellationToken cancellationToken) =>
        SendAsync(EditorWireProtocol.TargetBound(bound), cancellationToken);

    /// <summary>Whether the plugin's socket is still open.</summary>
    public bool IsOpen => _socket.State == WebSocketState.Open;

    /// <summary>Whether the panel failed to prove it is this machine's installed plugin; the hub then closes the connection.</summary>
    public bool IsRefused { get; private set; }

    /// <summary>Marks the connection as one the hub will not serve.</summary>
    public void Refuse() => IsRefused = true;

    /// <summary>Sends one message to the plugin, waiting for any send already in progress to finish first.</summary>
    public async Task SendAsync(JsonNode message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());

        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _socket
                .SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Short session summary for diagnostics.</summary>
    public JsonObject Describe() => new()
    {
        ["connectionId"] = ConnectionId,
        ["fileKey"] = FileKey,
        ["fileName"] = FileName,
        ["currentPage"] = CurrentPage,
        ["editorType"] = EditorType,
        ["pluginVersion"] = PluginVersion,
        ["connectedAt"] = ConnectedAt.ToString("O"),
        ["open"] = IsOpen,
    };
}
