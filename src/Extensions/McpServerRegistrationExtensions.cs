using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Composition root: lists the features to wire; how HttpClient, sockets or the database are configured is known one level down.</summary>
public static class McpServerRegistrationExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder ConfigureMcpServer()
        {
            builder.Logging.UseStderr();

            var config = builder.AddServerConfiguration();
            var endpoint = builder.ReserveBridgePort(config.Bridge);

            builder.Services
                .AddSingleton(endpoint)
                .AddIntegration()
                .AddSnapshotPersistence()
                .AddReadPipeline()
                .AddIdleShutdownWatcher(config.IdleTimeoutMinutes)
                .AddMcpTransport();

            return builder;
        }
    }
}
