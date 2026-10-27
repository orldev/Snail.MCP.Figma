using System.Net;
using System.Net.WebSockets;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Persistence.Sqlite;
using Snail.MCP.Figma.Adapters.Rest;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Chaos;

/// <summary>The bridge under races and cancellation: an agent leaving while its command reads its departure, a naming that failed once, a call cancelled before its send.</summary>
[Collection(nameof(BridgeHostFixture))]
public class BridgeConcurrencyChaosTests(BridgeHostFixture bridge)
{
    private const string FileUrl = "https://www.figma.com/design/ABC123XYZ/Design-System";

    private readonly BridgeHostFixture _bridge = bridge;

    /// <summary>Breaks <see cref="AgentSlot.Departure"/>: Release swaps the source, cancels and disposes the old one, and a reader that took the old field reads <c>Token</c> of a disposed source.</summary>
    /// <remarks>The router reads Departure for every command of the agent; a session closing at that moment turns the command into an ObjectDisposedException no tool guard catches.</remarks>
    [Fact]
    public async Task AgentSlot_ReleasedWhileCommandsReadDeparture_NeverThrowsObjectDisposed()
    {
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { DataDirectory = TestFolder.Create() }, NullLogger<AgentRoster>.Instance);
        var agent = roster.Default;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var readers = Enumerable.Range(0, 2).Select(reader => Task.Run(() =>
        {
            while (!deadline.IsCancellationRequested)
            {
                _ = agent.Departure;
            }
        }));

        var releaser = Task.Run(() =>
        {
            while (!deadline.IsCancellationRequested)
            {
                agent.Release();
            }
        });

        var exception = await Record.ExceptionAsync(() => Task.WhenAll([.. readers, releaser]));

        Assert.Null(exception);
    }

    /// <summary>Breaks <see cref="AgentSlot.NameOnceAsync"/>: a naming that failed once is cached as a faulted task, and every later call of that agent fails with it until the session ends.</summary>
    [Fact]
    public async Task AgentSlot_FirstNamingFaults_LaterCallsAreNotPoisoned()
    {
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        var roster = new AgentRoster(registry, new ServerConfig { DataDirectory = TestFolder.Create() }, NullLogger<AgentRoster>.Instance);
        var agent = roster.Default;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.NameOnceAsync(() => Task.FromException(new InvalidOperationException("The transport closed while roots were listed."))));

        var exception = await Record.ExceptionAsync(() => agent.NameOnceAsync(() => Task.CompletedTask));

        Assert.Null(exception);
    }

    /// <summary>Breaks the router's failure labels: a call the client cancelled before its send is reported as "could not send to the plugin", which the tool guard turns into "the plugin is not connected".</summary>
    [Fact]
    public async Task EditorCommandRouter_CancelledBeforeSend_ThrowsOperationCanceled()
    {
        var router = new EditorCommandRouter(new EditorLinkOptions { RequestTimeoutSeconds = 5 }, NullLogger<EditorCommandRouter>.Instance);
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var session = new EditorSession(socket, "chaos-connection");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exception = await Record.ExceptionAsync(() => router.SendAsync(session, null, "PING", cancellationToken: cancelled.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    /// <summary>Breaks <see cref="OpenDocumentRegistry"/>: a session that names a second file keeps the first key mapped to itself, so a write for the first file goes to the second one, and Remove never clears it.</summary>
    [Fact]
    public void OpenDocumentRegistry_SessionRenamesItsFile_OldKeyNoLongerResolves()
    {
        var registry = new OpenDocumentRegistry(NullLogger<OpenDocumentRegistry>.Instance);
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var session = new EditorSession(socket, "chaos-connection");

        registry.Register(session, "FILE_A", "Draft");
        registry.Register(session, "FILE_B", "Design System");
        registry.Remove(session);

        Assert.Null(registry.Find("FILE_A"));
    }

    /// <summary>Breaks the hub's memory: a message is buffered whole however long it runs, so one endless message from anything that reaches the bridge eats the memory of every agent; over the ceiling it must be dropped and the connection kept.</summary>
    [Fact]
    public async Task EditorSocketHub_MessageOverTheCeiling_IsDroppedAndTheConnectionLives()
    {
        var ceiling = _bridge.Hub.MaxMessageBytes;
        _bridge.Hub.MaxMessageBytes = 64 * 1024;
        using var socket = new ClientWebSocket();

        try
        {
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_bridge.Port}/"), CancellationToken.None);
            var padding = new string('x', 256 * 1024);

            await SendAsync(socket, $$$"""{"type":"FILE_INFO","data":{"fileKey":"OVERSIZED","fileName":"Too big","token":"{{{_bridge.Token.Value}}}","padding":"{{{padding}}}"}}""");
            await SendAsync(socket, $$$"""{"type":"FILE_INFO","data":{"fileKey":"AFTERWARDS","fileName":"Normal","token":"{{{_bridge.Token.Value}}}"}}""");
            await BridgeHostFixture.WaitUntilAsync(() => _bridge.Registry.Find("AFTERWARDS") is not null, "the connection did not survive the oversized message");

            Assert.Null(_bridge.Registry.Find("OVERSIZED"));
        }
        finally
        {
            _bridge.Hub.MaxMessageBytes = ceiling;
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }
    }

    /// <summary>Breaks the bridge's trust: Origin <c>null</c> — what any page in a sandboxed iframe sends — was enough to register a file, take its commands and answer them; a panel must prove it is this machine's installed plugin.</summary>
    [Fact]
    public async Task EditorSocketHub_PanelWithoutTheToken_IsRefused()
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "null");
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_bridge.Port}/"), CancellationToken.None);

        await SendAsync(socket, """{"type":"FILE_INFO","data":{"fileKey":"POSER","fileName":"Looks real"}}""");
        var isClosed = await IsClosedByServerAsync(socket);

        Assert.True(isClosed, "the connection was not closed");
        Assert.Null(_bridge.Registry.Find("POSER"));
    }

    /// <summary>Breaks command routing: a reply was matched by its id alone, and ids are a counter and a clock, so a second connection could answer a command sent into the first file.</summary>
    [Fact]
    public async Task EditorCommandRouter_ReplyFromAnotherConnection_DoesNotAnswerTheCommand()
    {
        var router = new EditorCommandRouter(new EditorLinkOptions { RequestTimeoutSeconds = 5 }, NullLogger<EditorCommandRouter>.Instance);
        using var targetSocket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        using var otherSocket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var target = new EditorSession(targetSocket, "target-connection");
        var other = new EditorSession(otherSocket, "other-connection");
        var command = router.SendAsync(target, null, "PING", timeout: TimeSpan.FromSeconds(2));
        await BridgeHostFixture.WaitUntilAsync(() => router.PendingCount == 1, "the command was not sent");
        var guessedId = $"snail_1_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var isTaken = router.TryComplete(other, new JsonObject { ["id"] = guessedId, ["result"] = "forged" }) ||
                      router.TryComplete(other, new JsonObject { ["id"] = PendingId(router), ["result"] = "forged" });

        Assert.False(isTaken);
        await Assert.ThrowsAsync<TimeoutException>(() => command);
    }

    private static string PendingId(EditorCommandRouter router) =>
        typeof(EditorCommandRouter)
            .GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(router) is System.Collections.IDictionary pending
            ? pending.Keys.Cast<string>().Single()
            : throw new InvalidOperationException("the router keeps its pending commands elsewhere");

    private static async Task<bool> IsClosedByServerAsync(ClientWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, deadline.Token);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return true;
                }
            }

            return socket.State is WebSocketState.CloseReceived or WebSocketState.Closed;
        }
        catch (WebSocketException)
        {
            return true;
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string message) =>
        socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>Breaks the promise of architecture.md that a command whose file went away names that file: the tool guard replaced the message AgentSlot.Unavailable builds with a generic "the plugin is not connected".</summary>
    [Fact]
    public async Task EditorToolBase_TargetFileDisconnected_NamesTheFile()
    {
        var tools = new ComponentTools(_bridge.Gateway, NullLogger<ComponentTools>.Instance);
        _bridge.Agent.Return("GONEFILE123", "Checkout flow");

        try
        {
            var answer = JsonNode.Parse(await tools.SearchComponentsAsync())!;

            Assert.Contains("Checkout flow", answer["error"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            _bridge.Agent.Release();
        }
    }

    /// <summary>Breaks the doubt about the variables snapshot: a VARIABLES_DATA the panel pushes after a write — read by the plugin before it, since another server's REFRESH was queued first — clears the doubt the write raised, and the agent does not see its own new variable for thirty seconds.</summary>
    /// <remarks>ui.html broadcasts every REFRESH result to all servers; RememberVariables cannot tell a snapshot taken before the write from one taken after it.</remarks>
    [Fact]
    public void EditorSession_SnapshotPushedAfterWrite_StaysDoubted()
    {
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var session = new EditorSession(socket, "chaos-connection");
        var beforeTheWrite = new JsonObject { ["variables"] = new JsonArray(), ["variableCollections"] = new JsonArray() };
        session.RememberVariables(beforeTheWrite.DeepClone());

        session.DoubtVariables();
        session.RememberVariables(beforeTheWrite.DeepClone());

        Assert.False(session.HasFreshVariables);
    }

    /// <summary>Breaks cancellation of figma_get_file_for_plugin: the tool calls ExecuteAsync without its token, so a call the client already gave up on still spends a REST request (up to 60 s and three attempts).</summary>
    [Fact]
    public async Task ReadTools_GetFileForPluginCancelled_SendsNoRequest()
    {
        var handler = new CountingHandler();
        var tools = ReadTools(handler);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Record.ExceptionAsync(() => tools.GetFileForPluginAsync(FileUrl, cancellationToken: cancelled.Token));

        Assert.Equal(0, handler.Requests);
    }

    private ReadTools ReadTools(HttpMessageHandler handler)
    {
        var config = new ServerConfig { DataDirectory = TestFolder.Create(), AccessToken = "figd_chaos-token", Cache = { Enabled = false } };
        var api = new RestDocumentGateway(new OneHandlerFactory(handler), config, NullLogger<RestDocumentGateway>.Instance, new QuotaGate(), static (_, _) => Task.CompletedTask);
        var database = new DisabledSnapshotDatabase();
        var nodes = new SqliteNodeSnapshotStore(database, config.Cache, NullLogger<SqliteNodeSnapshotStore>.Instance);
        var images = new SqliteRenderSnapshotStore(database, config.Cache, NullLogger<SqliteRenderSnapshotStore>.Instance);
        var versions = new SqliteDocumentVersionLedger(database, nodes, images, NullLogger<SqliteDocumentVersionLedger>.Instance);
        var sanitization = new SanitizationOptions { LogMetrics = false };
        var sanitizer = new NodeSanitizer(sanitization, NullLogger<NodeSanitizer>.Instance);

        return new ReadTools(
            api,
            new DocumentReader(api, nodes, versions, sanitizer, config.Cache, NullLogger<DocumentReader>.Instance),
            new DocumentRenderer(api, images, versions, NullLogger<DocumentRenderer>.Instance),
            _bridge.Gateway,
            new CaptureStore(config, NullLogger<CaptureStore>.Instance),
            new ActiveDocumentTracker(),
            sanitizer,
            sanitization,
            config.Bridge,
            NullLogger<ReadTools>.Instance);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _requests;

        public int Requests => _requests;

        /// <summary>Behaves as the sockets handler does: a request whose token is already cancelled never leaves.</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
            }

            Interlocked.Increment(ref _requests);

            var document = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"name":"File","document":{"id":"0:0","type":"DOCUMENT","children":[]}}"""),
            };

            return Task.FromResult(document);
        }
    }

    private sealed class OneHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
