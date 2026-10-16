using Microsoft.Extensions.Hosting;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Probes the bridge port range from the server side, where a closed port is silent, and pushes the live set to connected plugins as <c>PORTS_ALIVE</c>; ui.html then attaches to additional servers without probing ports from the browser, which would spam the DevTools console.</summary>
public sealed class SiblingDiscovery(
    EditorSocketHub hub,
    OpenDocumentRegistry registry,
    EditorLinkOptions options,
    IHttpClientFactory httpClients,
    ILogger<SiblingDiscovery> logger)
    : BackgroundService
{
    /// <summary>Name of the HTTP client that probes the other bridge ports, registered with a one-second timeout.</summary>
    public const string HttpClientName = "sibling-discovery";

    internal static readonly TimeSpan Cadence = TimeSpan.FromSeconds(45);

    /// <summary>Probes the port range every 45 seconds and tells connected plugins which other servers answer, until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Cadence);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await BroadcastAliveAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BroadcastAliveAsync(CancellationToken cancellationToken)
    {
        if (!hub.IsListening || registry.Sessions.Count == 0)
        {
            return;
        }

        var alive = await ProbeRangeAsync(cancellationToken).ConfigureAwait(false);

        if (alive.Count == 0)
        {
            return;
        }

        Log.SiblingsAlive(logger, string.Join(", ", alive));

        foreach (var session in registry.Sessions)
        {
            try
            {
                await session.SendAsync(EditorWireProtocol.PortsAlive(alive), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Log.PortsNotPushed(logger, session.ConnectionId, ex.Message);
            }
        }
    }

    private async Task<IReadOnlyList<int>> ProbeRangeAsync(CancellationToken cancellationToken)
    {
        var client = httpClients.CreateClient(HttpClientName);
        var probes = SiblingPorts(options, hub.Port).Select(port => ProbeAsync(client, port, cancellationToken));
        var results = await Task.WhenAll(probes).ConfigureAwait(false);

        return [.. results.Where(port => port is not null).Select(port => port!.Value)];
    }

    private static async Task<int?> ProbeAsync(HttpClient client, int port, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await client.GetStringAsync($"http://127.0.0.1:{port}/health", cancellationToken).ConfigureAwait(false);

            return IsBridgeHealth(JsonNode.Parse(payload)) ? port : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static IEnumerable<int> SiblingPorts(EditorLinkOptions options, int ownPort) =>
        Enumerable.Range(options.PortRangeStart, options.PortRangeEnd - options.PortRangeStart + 1)
            .Where(port => port != ownPort);

    /// <summary>The /health shape of this bridge: status ok plus version and port; anything else on the range is a foreign server.</summary>
    internal static bool IsBridgeHealth(JsonNode? payload) =>
        payload?["status"]?.GetValue<string>() == "ok"
        && payload["version"] is not null
        && payload["port"] is not null;
}
