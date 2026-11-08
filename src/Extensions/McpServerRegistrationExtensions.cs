using Microsoft.AspNetCore.Builder;
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
        /// <summary>The server a client starts for itself and talks to over stdio; it exits when its client has said nothing for a while and no plugin is connected.</summary>
        /// <remarks>The idle stop is Snail.Sdk's (<c>AddIdleShutdown</c>): any MCP message keeps the server up, and a connected plugin vetoes the stop, since the human may be working in Figma while the agent thinks. The setting goes through as it is: zero or less turns the stop off.</remarks>
        public WebApplicationBuilder ConfigureMcpServer(McpEndpoint endpoint)
        {
            var config = builder.AddServerConfiguration(endpoint);

            builder
                .AddFigma(config)
                .AddIdleShutdown(TimeSpan.FromMinutes(config.IdleTimeoutMinutes), IsPluginConnected)
                .AddMcpTransport();

            return builder;
        }

        /// <summary>The one server every agent on the machine shares, over Streamable HTTP on loopback; launchd keeps it running, so it has no idle watchdog, and it keeps the log launchd appends to within a size.</summary>
        /// <remarks>A background helper that fails is logged and left stopped rather than allowed to stop the host, which would cut every agent off from Figma at once. Where it listens is the endpoint's, read with <c>McpReach.LoopbackOnly</c> (<see cref="ServerHost"/>), which refuses an address beyond loopback, every interface and a start inside a container.</remarks>
        public WebApplicationBuilder ConfigureSharedMcpServer(McpEndpoint endpoint)
        {
            var config = builder.AddServerConfiguration(endpoint);

            builder.Services.Configure<HostOptions>(host => host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

            builder
                .AddFigma(config)
                .AddLogTrim(config.LogFile)
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
                .AddReadPipeline()
                .AddDesignSystemExtraction();
        }
    }

    /// <summary>Whether a plugin is connected to the agent's file: the human may be at work in Figma, so the server stays.</summary>
    private static bool IsPluginConnected(IServiceProvider services) =>
        services.GetRequiredService<IOpenDocuments>().HasConnection;
}
