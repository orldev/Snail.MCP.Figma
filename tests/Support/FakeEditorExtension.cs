using System.Net.WebSockets;
using System.Text;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Fake plugin behaving like <c>ui.html</c>: introduces itself with FILE_INFO, answers commands by requestId and sends events.</summary>
public sealed class FakeEditorExtension : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly List<JsonNode> _received = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _loop;
    private bool _isDisposed;

    public IReadOnlyList<JsonNode> Received
    {
        get { lock (_gate) { return [.. _received]; } }
    }

    /// <summary>How to answer a command: method name → result or error.</summary>
    public Dictionary<string, Func<JsonNode?, JsonNode?>> Handlers { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

    /// <summary>Commands the plugin takes and never answers, as a script that is still running.</summary>
    public HashSet<string> Silent { get; } = new(StringComparer.Ordinal);

    public async Task ConnectAsync(int port, string fileKey, string fileName, string path = "/", int protocol = 1, string? token = null)
    {
        await _socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}{path}"), CancellationToken.None);

        _loop = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_loop.Token));

        await SendAsync(new JsonObject
        {
            ["type"] = "FILE_INFO",
            ["data"] = new JsonObject
            {
                ["fileKey"] = fileKey,
                ["fileName"] = fileName,
                ["pluginVersion"] = "1.40.0",
                ["editorType"] = "figma",
                ["currentPage"] = "Page 1",
                ["protocol"] = protocol,
                ["token"] = token,
            },
        });
    }

    public Task SendEventAsync(string type, JsonObject data) =>
        SendAsync(new JsonObject { ["type"] = type, ["data"] = data });

    private async Task SendAsync(JsonNode message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];

        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (message is null)
                {
                    continue;
                }

                lock (_gate)
                {
                    _received.Add(message);
                }

                var id = message["id"]?.GetValue<string>();
                var method = message["method"]?.GetValue<string>();

                if (id is null || method is null)
                {
                    continue;
                }

                if (Silent.Contains(method))
                {
                    continue;
                }

                if (Errors.TryGetValue(method, out var error))
                {
                    await SendAsync(new JsonObject { ["id"] = id, ["error"] = error });
                    continue;
                }

                var payload = Handlers.TryGetValue(method, out var handler)
                    ? handler(message["params"])
                    : new JsonObject { ["success"] = true };

                await SendAsync(new JsonObject { ["id"] = id, ["result"] = payload });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    /// <summary>Says goodbye before the receive loop is cancelled: cancelling a receive aborts the socket, and a close started on an aborted socket throws instead of closing.</summary>
    /// <remarks>Only the output side is closed here; the loop reads the server's close frame and ends by itself, so the two never race for the one receive the socket allows. A test may drop the plugin itself before its <c>await using</c> does, so a second call does nothing.</remarks>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }

        if (_loop is not null)
        {
            await _loop.CancelAsync();
        }

        _socket.Dispose();
        _loop?.Dispose();
    }
}
