using System.Net.WebSockets;
using System.Text;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>One plugin connection — one file open in Figma.</summary>
public sealed class EditorSession : IOpenDocument
{
    /// <summary>Sends are serialized: WebSocket forbids concurrent SendAsync calls.</summary>
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private readonly WebSocket _socket;

    public EditorSession(WebSocket socket, string connectionId)
    {
        _socket = socket;
        ConnectionId = connectionId;
        ConnectedAt = DateTimeOffset.UtcNow;
        LastActivityAt = ConnectedAt;
    }

    public string ConnectionId { get; }

    /// <summary>Filled from <c>FILE_INFO</c>; until then the session is unidentified.</summary>
    public string? FileKey { get; set; }

    public string? FileName { get; set; }

    public string? CurrentPage { get; set; }

    public string? CurrentPageId { get; set; }

    public string? PluginVersion { get; set; }

    public string? EditorType { get; set; }

    public DateTimeOffset ConnectedAt { get; }

    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Last known selection — the source of coordinates for mutations.</summary>
    public JsonNode? Selection { get; set; }

    /// <summary>Variables snapshot pushed by the plugin; mirrors the cache ui.html itself serves, so reads skip the socket.</summary>
    public JsonNode? VariablesSnapshot { get; set; }

    /// <summary>Ring buffer of the plugin sandbox console.</summary>
    public RingBuffer<JsonNode> ConsoleLogs { get; } = new(1000);

    /// <summary>Ring buffer of document-change events.</summary>
    public RingBuffer<JsonNode> DocumentChanges { get; } = new(200);

    /// <summary>Ring buffer of description and annotation edits, which REST version snapshots never expose.</summary>
    public RingBuffer<JsonNode> MetadataChanges { get; } = new(200);

    public bool IsOpen => _socket.State == WebSocketState.Open;

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
