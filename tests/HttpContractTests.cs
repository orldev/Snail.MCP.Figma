using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Snail.MCP.Figma.Tests;

/// <summary>The shared server as the machine runs it: one process over HTTP on loopback, an agent for every client named after the folder it works in, each agent's commands in its own file, and no browser let through.</summary>
/// <remarks>The panel is a fake plugin of protocol 2 on the real bridge: what it is told in <c>AGENTS</c> is what the human would see.</remarks>
#pragma warning disable MCP9005
public sealed class HttpContractTests : IAsyncLifetime
{
    private const int BridgeRangeStart = 9420;

    private const int BridgeRangeEnd = 9430;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly string _dataDirectory = TestFolder.Create();

    private HttpServerProgram _server = null!;

    private Uri _endpoint = null!;

    private int _bridgePort;

    public Task InitializeAsync() => StartServerAsync();

    /// <summary>The shared server as launchd starts it, through Snail.Sdk's <see cref="HttpServerProgram"/>: a free loopback port, its own home, no settings file, and ready once <c>/healthz</c> answers.</summary>
    private async Task StartServerAsync()
    {
        _server = await HttpServerProgram.StartAsync("Snail.MCP.Figma", "SNAIL_FIGMA_SERVER", new Dictionary<string, string?>
        {
            ["SNAIL_MCP_FIGMA_DATA_DIRECTORY"] = _dataDirectory,
            ["SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START"] = BridgeRangeStart.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_END"] = BridgeRangeEnd.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SNAIL_MCP_FIGMA_ACCESS_TOKEN"] = null,
        });
        _endpoint = _server.Endpoint;

        using var waiting = new CancellationTokenSource(Timeout);
        using var http = new HttpClient();

        _bridgePort = await FindBridgeAsync(http, waiting.Token);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    /// <summary>A restart drops every session; the agent that comes back from the same folder is in its file again without a navigate.</summary>
    [Fact]
    public async Task SharedServer_RestartedAfterACrash_AgentKeepsItsFile()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        var before = await ConnectAsync("Keynex", cancellation.Token);

        await using (var panel = await ConnectPanelAsync("survivor"))
        {
            await before.CallToolAsync("figma_navigate", new Dictionary<string, object?> { ["fileUrlOrKey"] = "survivor" }, cancellationToken: cancellation.Token);
        }

        await _server.DisposeAsync();
        await StartServerAsync();
        await using var after = await ConnectAsync("Keynex", cancellation.Token);

        var answer = await after.CallToolAsync("figma_get_status", cancellationToken: cancellation.Token);
        var status = JsonNode.Parse(answer.Text())!;

        Assert.Equal("survivor", status["data"]!["activeFileKey"]!.GetValue<string>());

        try
        {
            await before.DisposeAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or McpException or OperationCanceledException)
        {
        }
    }

    /// <summary>Each client is an agent of its own, and the panel calls it by its project, not by the client's name.</summary>
    [Fact]
    public async Task SharedServer_TwoClientsConnect_PanelNamesEachAfterItsFolder()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var panel = await ConnectPanelAsync("names");
        await using var keynex = await ConnectAsync("Keynex", cancellation.Token);
        await using var hub = await ConnectAsync("Vertical.Hub", cancellation.Token);

        await keynex.CallToolAsync("figma_get_status", cancellationToken: cancellation.Token);
        await hub.CallToolAsync("figma_get_status", cancellationToken: cancellation.Token);

        await BridgeHostFixture.WaitUntilAsync(
            () => Names(panel).Contains("Keynex") && Names(panel).Contains("Vertical.Hub"),
            "the panel was not told the agents by their folders");
    }

    /// <summary>Two agents, two files: what each one runs reaches its own file and nobody else's.</summary>
    [Fact]
    public async Task SharedServer_TwoAgentsInTwoFiles_CommandsReachOnlyTheirOwnFile()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var web = await ConnectPanelAsync("web");
        await using var mobile = await ConnectPanelAsync("mobile");
        await using var keynex = await ConnectAsync("Keynex", cancellation.Token);
        await using var hub = await ConnectAsync("Vertical.Hub", cancellation.Token);

        await keynex.CallToolAsync("figma_navigate", new Dictionary<string, object?> { ["fileUrlOrKey"] = "web" }, cancellationToken: cancellation.Token);
        await hub.CallToolAsync("figma_navigate", new Dictionary<string, object?> { ["fileUrlOrKey"] = "mobile" }, cancellationToken: cancellation.Token);
        await keynex.CallToolAsync("figma_execute", new Dictionary<string, object?> { ["code"] = "return 1;" }, cancellationToken: cancellation.Token);

        Assert.Single(web.Received, message => message["method"] is not null);
        Assert.DoesNotContain(mobile.Received, message => message["method"] is not null);
        await BridgeHostFixture.WaitUntilAsync(() => IsHere(web, "Keynex") && !IsHere(web, "Vertical.Hub"), "the web panel was told the wrong agents");
        await BridgeHostFixture.WaitUntilAsync(() => IsHere(mobile, "Vertical.Hub") && !IsHere(mobile, "Keynex"), "the mobile panel was told the wrong agents");
    }

    /// <summary>A client that closes its session takes its agent out of every panel.</summary>
    [Fact]
    public async Task SharedServer_ClientClosesItsSession_AgentLeavesThePanel()
    {
        using var cancellation = new CancellationTokenSource(Timeout);
        await using var panel = await ConnectPanelAsync("leaving");
        var leaving = await ConnectAsync("Leaving.Project", cancellation.Token);
        await leaving.CallToolAsync("figma_get_status", cancellationToken: cancellation.Token);
        await BridgeHostFixture.WaitUntilAsync(() => Names(panel).Contains("Leaving.Project"), "the agent did not arrive");

        await leaving.DisposeAsync();

        await BridgeHostFixture.WaitUntilAsync(() => !Names(panel).Contains("Leaving.Project"), "the agent stayed after its session ended");
    }

    /// <summary>The sessionless revision carries nothing that tells one window of a client from another, so it is refused in the way that makes Claude Code fall back to initialize and a session of its own.</summary>
    [Fact]
    public async Task SharedServer_SessionlessRequest_IsSentBackToInitialize()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"claude-code","version":"2.1.296"},"io.modelcontextprotocol/clientCapabilities":{"roots":{"listChanged":true}}}}}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("mcp-protocol-version", "2026-07-28");
        request.Headers.Add("mcp-method", "server/discover");

        using var response = await http.SendAsync(request);
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

        Assert.Equal(-32022, answer["error"]!["code"]!.GetValue<int>());
        Assert.Contains("2025-11-25", answer["error"]!["data"]!["supported"]!.AsArray().Select(version => version!.GetValue<string>()));
    }

    /// <summary>A browser's request, with an <c>Origin</c> of a page or <c>null</c>, is refused at the MCP endpoint, and one without it is not.</summary>
    [Fact]
    public async Task SharedServer_RequestWithBrowserOrigin_IsRefused() =>
        Assert.Empty(await HttpGuards.ProblemsAsync(_endpoint));

    /// <summary>The names the panel was last told, as it keeps them.</summary>
    private static IReadOnlyList<string> Names(FakeEditorExtension panel) => [.. Agents(panel).Select(agent => agent!["name"]!.GetValue<string>())];

    private static bool IsHere(FakeEditorExtension panel, string name) =>
        Agents(panel).Any(agent => agent!["name"]!.GetValue<string>() == name && agent["bound"]!.GetValue<bool>());

    private static JsonArray Agents(FakeEditorExtension panel) =>
        panel.Received.LastOrDefault(message => message["type"]?.GetValue<string>() == "AGENTS")?["data"]?["agents"]?.AsArray() ?? [];

    private async Task<FakeEditorExtension> ConnectPanelAsync(string fileKey)
    {
        var panel = new FakeEditorExtension();
        var token = await File.ReadAllTextAsync(Path.Combine(_dataDirectory, Snail.MCP.Figma.Adapters.Editor.BridgeToken.FileName));
        await panel.ConnectAsync(_bridgePort, fileKey, fileKey, protocol: 2, token: token.Trim());
        await BridgeHostFixture.WaitUntilAsync(
            () => panel.Received.Any(message => message["type"]?.GetValue<string>() == "AGENTS"),
            "the panel of protocol 2 was not told the agents");

        return panel;
    }

    /// <summary>A client of the shared server working in the folder <paramref name="project"/>.</summary>
    /// <remarks>Speaks the 2025-06-18 revision: a session is opened by initialize, which is where roots live; clients of today still do.</remarks>
    private Task<McpClient> ConnectAsync(string project, CancellationToken cancellationToken)
    {
        var folder = new Uri(Path.Combine(_dataDirectory, project)).AbsoluteUri;
        var options = new McpClientOptions
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new ClientCapabilities { Roots = new RootsCapability() },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (_, _) => ValueTask.FromResult(new ListRootsResult { Roots = [new Root { Uri = folder }] }),
            },
        };

        return McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = _endpoint }),
            options,
            cancellationToken: cancellationToken);
    }

    /// <summary>The bridge takes the first free port of its range; the one that answers /health is it.</summary>
    private static async Task<int> FindBridgeAsync(HttpClient http, CancellationToken cancellationToken)
    {
        while (true)
        {
            for (var port = BridgeRangeStart; port <= BridgeRangeEnd; port++)
            {
                try
                {
                    using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health"), cancellationToken);

                    if (response.IsSuccessStatusCode)
                    {
                        return port;
                    }
                }
                catch (HttpRequestException)
                {
                }
            }

            await Task.Delay(100, cancellationToken);
        }
    }
}
