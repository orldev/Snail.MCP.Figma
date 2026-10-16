using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Kestrel and HTTP endpoints of the bridge: the WebSocket upgrade and /health for the plugin watchdog.</summary>
public static class EditorLinkHostingExtensions
{
    /// <summary>How often the bridge pings a panel, and how long it waits for the pong before it gives the connection up.</summary>
    /// <remarks>A Figma window that froze or a laptop that slept leaves a socket open on our side with nobody behind it: the file stayed connected and every command into it ran into its timeout. Chromium answers a ping by itself, so a live panel never notices.</remarks>
    internal static readonly TimeSpan BridgeKeepAlive = TimeSpan.FromSeconds(20);

    extension(WebApplicationBuilder builder)
    {
        /// <summary>Picks the port before Kestrel is configured, see <see cref="EditorLinkEndpoint"/>.</summary>
        internal EditorLinkEndpoint ReserveBridgePort(EditorLinkOptions options)
        {
            var port = PortDiscovery.FindAvailablePort(options);

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                if (port is { } value)
                {
                    kestrel.ListenLocalhost(value);
                }
            });

            return new EditorLinkEndpoint(port, options.Path);
        }
    }

    extension(WebApplication app)
    {
        public WebApplication UseBridge()
        {
            var endpoint = app.Services.GetRequiredService<EditorLinkEndpoint>();
            var hub = app.Services.GetRequiredService<EditorSocketHub>();
            hub.Port = endpoint.Port ?? 0;
            hub.ServerVersion = ThisAssembly.InformationalVersion;
            hub.ExpectedPluginVersion = app.Services.GetService<EditorExtensionInstaller>()?.InstalledPluginVersion;

            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = BridgeKeepAlive, KeepAliveTimeout = BridgeKeepAlive });

            app.MapWhen(
                ctx => ctx.Connection.LocalPort == endpoint.Port && ctx.Request.Path == endpoint.Path && ctx.WebSockets.IsWebSocketRequest,
                branch => branch.Run(hub.HandleAsync));

            var health = app.MapGet("/health", (HttpContext ctx, OpenDocumentRegistry registry, AgentRoster agents) =>
            {
                var origin = ctx.Request.Headers.Origin.ToString();

                if (!string.IsNullOrEmpty(origin) && EditorSocketHub.IsAllowedOrigin(origin))
                {
                    ctx.Response.Headers.AccessControlAllowOrigin = origin;
                }

                return Results.Json(
                    new BridgeHealth("ok", ThisAssembly.InformationalVersion, endpoint.Port, registry.Sessions.Count, agents.Default.ActiveFileKey, agents.Slots.Count),
                    BridgeHealthJson.Default.BridgeHealth);
            });

            if (endpoint.Port is { } port)
            {
                health.RequireHost($"*:{port}");
            }

            return app;
        }

        /// <summary>Runs the server until it stops; a stop asked for while the server was still starting is a stop, not a failure.</summary>
        /// <remarks>Over stdio the MCP transport starts before Kestrel. A client that closes its input at once — the install's warm-up run feeds it <c>/dev/null</c> — asks the host to stop while Kestrel is still binding the bridge port, and the bind gives up with a cancellation that ended the process with an abort, code 134.
        /// <para>The token is taken before the run: a failed start disposes the host, and asking it for its lifetime afterwards would throw inside the filter, which reads as a no.</para></remarks>
        public async Task RunUntilStoppedAsync()
        {
            var stopping = app.Lifetime.ApplicationStopping;

            try
            {
                await app.RunAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
            }
        }

        /// <summary>Maps the shared server's MCP endpoint on its own port, behind the refusal of every request a browser makes, and writes the agents' files down as the server stops.</summary>
        /// <remarks>The refusal holds on the MCP port only. The bridge in the same process is called by the plugin from inside Figma, which always sends an <c>Origin</c>; a refusal for the whole host, as the shared servers without a bridge map it, would cut the plugin off.</remarks>
        public WebApplication UseSharedMcp()
        {
            var endpoint = app.Services.GetRequiredService<SharedMcpEndpoint>();
            var agents = app.Services.GetRequiredService<AgentRoster>();

            app.Lifetime.ApplicationStopping.Register(agents.RememberAll);

            app.UseWhen(ctx => ctx.Connection.LocalPort == endpoint.Port, branch => branch.Use(RefuseBrowsers));
            app.MapMcp(McpHttpHost.DefaultPath).RequireHost($"*:{endpoint.Port}");

            return app;
        }
    }

    /// <summary>A page in a browser always sends an <c>Origin</c> and an agent's client never does, so no web page can reach the MCP endpoint, not even through a name rebound to 127.0.0.1.</summary>
    internal static Task RefuseBrowsers(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.Origin.Any())
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}

/// <summary>What /health answers: the plugin tells this bridge from a foreign server by it, and sibling servers find each other with it.</summary>
/// <remarks>The plugin recognises the bridge by <c>connectedFiles</c>, and a sibling by <c>status</c>, <c>version</c> and <c>port</c>; the names are the wire contract.</remarks>
public sealed record BridgeHealth(string Status, string Version, int? Port, int ConnectedFiles, string? ActiveFile, int Agents);

/// <summary>The health answer, described by the source generator.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BridgeHealth))]
internal sealed partial class BridgeHealthJson : JsonSerializerContext;
