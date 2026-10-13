using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Kestrel and HTTP endpoints of the bridge: the WebSocket upgrade and /health for the plugin watchdog.</summary>
public static class EditorLinkHostingExtensions
{
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

            app.UseWebSockets();

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

                return Results.Json(new
                {
                    status = "ok",
                    version = ThisAssembly.InformationalVersion,
                    port = endpoint.Port,
                    connectedFiles = registry.Sessions.Count,
                    activeFile = agents.Default.ActiveFileKey,
                    agents = agents.Slots.Count,
                });
            });

            if (endpoint.Port is { } port)
            {
                health.RequireHost($"*:{port}");
            }

            return app;
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
