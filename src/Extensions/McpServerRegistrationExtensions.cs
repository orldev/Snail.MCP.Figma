using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Composition root: lists the features to wire; how HttpClient, sockets or the database are configured is known one level down.</summary>
/// <remarks>The two ways to run are two methods, never one method reading a value: what is built is named by what was called.</remarks>
public static class McpServerRegistrationExtensions
{
    extension(WebApplicationBuilder builder)
    {
        /// <summary>The server a client starts for itself and talks to over stdio; it exits when nobody has used it for a while.</summary>
        public WebApplicationBuilder ConfigureMcpServer(ServerConfig config)
        {
            builder.Logging.UseStderr();

            builder
                .AddFigma(config)
                .AddIdleShutdownWatcher(config.IdleTimeoutMinutes)
                .AddMcpTransport();

            return builder;
        }

        /// <summary>The one server every agent on the machine shares, over Streamable HTTP on loopback; launchd keeps it running, so it has no idle watchdog.</summary>
        /// <remarks>A background helper that fails is logged and left stopped rather than allowed to stop the host, which would cut every agent off from Figma at once.</remarks>
        /// <exception cref="ArgumentException">The configured address is not a loopback one.</exception>
        public WebApplicationBuilder ConfigureSharedMcpServer(ServerConfig config)
        {
            builder.Logging.UseStderr().AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

            var endpoint = SharedMcpEndpoint.From(config.Url ?? ServerConfig.DefaultUrl);

            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(endpoint.Address, endpoint.Port));
            builder.Services.Configure<HostOptions>(host => host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

            builder
                .AddFigma(config)
                .AddSingleton(endpoint)
                .AddMcpHttpTransport();

            return builder;
        }

        /// <summary>What both ways share: the bridge to the plugin, the snapshot cache and the read pipeline.</summary>
        private IServiceCollection AddFigma(ServerConfig config)
        {
            var bridge = builder.ReserveBridgePort(config.Bridge);

            return builder.Services
                .AddSingleton(bridge)
                .AddIntegration()
                .AddSnapshotPersistence()
                .AddReadPipeline();
        }
    }
}
