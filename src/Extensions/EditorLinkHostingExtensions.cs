using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

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
                ctx => ctx.Request.Path == endpoint.Path && ctx.WebSockets.IsWebSocketRequest,
                branch => branch.Run(hub.HandleAsync));

            app.MapGet("/health", (HttpContext ctx, OpenDocumentRegistry registry) =>
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
                    activeFile = registry.ActiveFileKey,
                });
            });

            return app;
        }
    }
}
