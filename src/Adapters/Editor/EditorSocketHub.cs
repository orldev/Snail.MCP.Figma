using System.Net.WebSockets;
using Microsoft.AspNetCore.Http;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>WebSocket hub: accepts plugin connections, routes replies and dispatches events; the only path that can write into Figma.</summary>
public sealed class EditorSocketHub(
    OpenDocumentRegistry registry,
    AgentRoster agents,
    EditorCommandRouter router,
    SnapshotInvalidator invalidation,
    EditorLinkOptions options,
    BridgeToken token,
    ILogger<EditorSocketHub> logger)
    : ILiveEditorLink
{
    /// <summary>Screenshots and large component trees reach tens of megabytes.</summary>
    private const int ReceiveBufferSize = 64 * 1024;

    /// <summary>The largest plugin message the hub reads; a larger one is dropped as it arrives and the connection stays.</summary>
    /// <remarks>A screenshot at 4x of a full page comes to about 70 MB in base64. Without a ceiling, one endless message — the bridge admits any local process and any page with Origin <c>null</c> — took every agent of the server down with the memory it ate.</remarks>
    public const int DefaultMaxMessageBytes = 128 * 1024 * 1024;

    /// <summary>The ceiling in force; a test lowers it to send an oversized message cheaply.</summary>
    public int MaxMessageBytes { get; set; } = DefaultMaxMessageBytes;

    /// <summary>Port the hub actually listens on; zero means the bridge is down.</summary>
    public int Port { get; set; }

    /// <summary>Version of the bundle installed on disk; a session reporting another version gets the update banner.</summary>
    public string? ExpectedPluginVersion { get; set; }

    /// <summary>Version this server announces to every plugin in its hello; the assembly's version once the bridge is set up.</summary>
    public string ServerVersion { get; set; } = "0.0.0";

    /// <summary>Whether the hub has a port and accepts plugin connections.</summary>
    public bool IsListening => Port > 0;

    /// <inheritdoc/>
    public string? Address => Port > 0 ? $"ws://127.0.0.1:{Port}{options.Path}" : null;

    /// <summary>Handles the WebSocket endpoint.</summary>
    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection expected");

            return;
        }

        var origin = context.Request.Headers.Origin.ToString();

        if (!IsAllowedOrigin(origin))
        {
            Log.OriginRejected(logger, origin);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = new EditorSession(socket, context.Connection.Id);

        try
        {
            await session.SendAsync(EditorWireProtocol.ServerHello(Port, ServerVersion, options.AgentName), context.RequestAborted);
            await ReceiveLoopAsync(session, socket, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            Log.ConnectionClosed(logger, ex.Message);
        }
        finally
        {
            registry.Remove(session);
            router.FailFor(session, $"The plugin in {session.FileName ?? session.FileKey ?? "the file"} disconnected before it answered.");
        }
    }

    /// <summary>Reads the plugin's messages until the socket closes; a message the hub cannot handle is logged and skipped, never allowed to end the connection.</summary>
    /// <remarks>On a shared server one file's connection carries every agent working there: a field of an unexpected type in one event must not cut all of them off from the file.</remarks>
    private async Task ReceiveLoopAsync(EditorSession session, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var payload = new MemoryStream();
            var isOversized = false;
            long length = 0;
            WebSocketReceiveResult received;

            do
            {
                received = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }

                length += received.Count;
                isOversized |= length > MaxMessageBytes;

                if (!isOversized)
                {
                    await payload.WriteAsync(buffer.AsMemory(0, received.Count), cancellationToken).ConfigureAwait(false);
                }
            }
            while (!received.EndOfMessage);

            if (isOversized)
            {
                Log.MessageTooLarge(logger, length, MaxMessageBytes, session.FileKey);

                continue;
            }

            try
            {
                await DispatchAsync(session, payload, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not WebSocketException)
            {
                Log.MessageFailed(logger, ex, session.FileKey);
            }

            if (session.IsRefused)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "not this machine's plugin", cancellationToken)
                    .ConfigureAwait(false);

                return;
            }
        }
    }

    private async Task DispatchAsync(EditorSession session, MemoryStream payload, CancellationToken cancellationToken)
    {
        JsonNode? message;

        try
        {
            message = EditorWireProtocol.Parse(payload);
        }
        catch (Exception ex)
        {
            Log.MessageNotParsed(logger, ex, (int)payload.Length);

            return;
        }

        if (message is null)
        {
            return;
        }

        session.LastActivityAt = DateTimeOffset.UtcNow;

        if (router.TryComplete(session, message))
        {
            return;
        }

        var type = message["type"]?.GetValue<string>();

        if (type is null)
        {
            return;
        }

        var data = message["data"];

        switch (type)
        {
            case EditorWireProtocol.Events.FileInfo:
                await HandleFileInfoAsync(session, data, cancellationToken).ConfigureAwait(false);
                break;

            case EditorWireProtocol.Events.SelectionChange:
                session.Selection = data?.DeepClone();
                session.LastInteractionAt = DateTimeOffset.UtcNow;
                break;

            case EditorWireProtocol.Events.PageChange:
                session.CurrentPage = data?["pageName"]?.GetValue<string>();
                session.CurrentPageId = data?["pageId"]?.GetValue<string>();
                session.LastInteractionAt = DateTimeOffset.UtcNow;
                break;

            case EditorWireProtocol.Events.BuildStop:
                await HandleBuildStopAsync(session, data, cancellationToken).ConfigureAwait(false);
                break;

            case EditorWireProtocol.Events.Peers:
                session.Peers = [.. (data?["agents"] as JsonArray ?? [])
                    .Select(agent => agent?.GetValue<string>())
                    .Where(agent => !string.IsNullOrWhiteSpace(agent))
                    .Select(agent => agent!)];
                break;

            case EditorWireProtocol.Events.RemarksClear:
                await agents.ClearRemarksAsync(session, Addressee(data), cancellationToken).ConfigureAwait(false);
                break;

            case EditorWireProtocol.Events.DocumentChange:
                await HandleDocumentChangeAsync(session, data, cancellationToken).ConfigureAwait(false);
                break;

            case EditorWireProtocol.Events.MetadataChange:
                if (data is not null)
                {
                    var change = data.DeepClone();
                    change["receivedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    session.MetadataChanges.Add(change);
                }

                break;

            case EditorWireProtocol.Events.ConsoleCapture:
                if (data is not null)
                {
                    session.ConsoleLogs.Add(data.DeepClone());
                }

                break;

            case EditorWireProtocol.Events.VariablesData:
                session.RememberVariables(data?.DeepClone());
                break;

            default:
                Log.EventUnhandled(logger, type);
                break;
        }
    }

    /// <summary>Queues the human's remark for the agents of this server that work in the file it was typed in.</summary>
    private async Task HandleBuildStopAsync(EditorSession session, JsonNode? data, CancellationToken cancellationToken)
    {
        session.LastInteractionAt = DateTimeOffset.UtcNow;

        var remark = new BuildInterruption(
            data?["remark"]?.GetValue<string>(),
            data?["nodes"] as JsonArray is { } selected ? [.. selected.Select(node => node!.DeepClone())] : [],
            DateTimeOffset.UtcNow);

        await agents.QueueRemarkAsync(session, remark, Addressee(data), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent a panel of protocol 2 addressed; <c>null</c> means every agent, which is what an older panel always means.</summary>
    private static string? Addressee(JsonNode? data) =>
        data?["agent"] is JsonValue value && value.TryGetValue<string>(out var agent) && !string.IsNullOrWhiteSpace(agent) ? agent : null;

    /// <summary>Identifies the file of a connection, once the panel has proved it is this machine's installed plugin (<see cref="BridgeToken"/>).</summary>
    /// <remarks>A panel without the secret — an older bundle, or a page posing as one — is told an update is waiting, which is what the human sees, and the connection is closed.</remarks>
    private async Task HandleFileInfoAsync(EditorSession session, JsonNode? data, CancellationToken cancellationToken)
    {
        var fileKey = data?["fileKey"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(fileKey))
        {
            Log.FileInfoWithoutKey(logger);

            return;
        }

        if (!token.IsMatchedBy(data?["token"] is JsonValue offered && offered.TryGetValue<string>(out var text) ? text : null))
        {
            Log.PanelNotRecognized(logger, fileKey);
            await session.SendAsync(EditorWireProtocol.PluginUpdateAvailable(), cancellationToken).ConfigureAwait(false);
            session.Refuse();

            return;
        }

        session.PluginVersion = data?["pluginVersion"]?.GetValue<string>();
        session.EditorType = data?["editorType"]?.GetValue<string>();
        session.CurrentPage = data?["currentPage"]?.GetValue<string>();
        session.Protocol = data?["protocol"] is JsonValue protocol && protocol.TryGetValue<int>(out var spoken) ? spoken : 1;

        registry.Register(session, fileKey, data?["fileName"]?.GetValue<string>());
        await agents.OnConnectedAsync(session, cancellationToken).ConfigureAwait(false);

        if (ExpectedPluginVersion is { } expected && session.PluginVersion != expected)
        {
            Log.PluginOutdated(logger, session.PluginVersion, expected);
            await session.SendAsync(EditorWireProtocol.PluginUpdateAvailable(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleDocumentChangeAsync(EditorSession session, JsonNode? data, CancellationToken cancellationToken)
    {
        if (data is null)
        {
            return;
        }

        session.DocumentChanges.Add(data.DeepClone());
        session.DoubtVariables();

        var changedNodeIds = (data["changedNodeIds"] as JsonArray)?
            .Select(node => node?.GetValue<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray() ?? [];

        var hasStyleChanges = data["hasStyleChanges"]?.GetValue<bool>() ?? false;

        await invalidation
            .OnDocumentChangedAsync(session.FileKey, changedNodeIds, hasStyleChanges, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Exact match, not a prefix check: <c>StartsWith</c> would accept <c>https://www.figma.com.attacker.example</c>.</summary>
    internal static bool IsAllowedOrigin(string origin) =>
        string.IsNullOrEmpty(origin) ||
        origin == "null" ||
        origin == "https://www.figma.com" ||
        origin == "https://figma.com";
}
