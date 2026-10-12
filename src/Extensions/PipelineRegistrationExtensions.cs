using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Hosting;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Use cases: the READ pipeline (read → sanitize → render), snapshot policy and the MCP transport.</summary>
public static class PipelineRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddReadPipeline()
        {
            services.AddSingleton<ActiveDocumentTracker>();
            services.AddSingleton<SnapshotInvalidator>();
            services.AddSingleton(provider => new VersionSnapshotCache(
                provider.GetRequiredService<CacheOptions>().VersionSnapshotEntries));
            services.AddSingleton<NodeSanitizer>();
            services.AddSingleton<DocumentReader>();
            services.AddSingleton<DocumentRenderer>();

            return services;
        }

        /// <summary>Driving adapter: tools are delivered to the model over stdio, the handshake carries the instructions, and every call marks the server as active.</summary>
        public IServiceCollection AddMcpTransport()
        {
            services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = ServerIdentity.Of(typeof(ThisAssembly).Assembly);
                    options.ServerInstructions = ServerInstructions.Text;
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly()
                .WithRequestFilters(filters => filters.AddCallToolFilter(TouchActivity));

            return services;
        }

        private static McpRequestHandler<CallToolRequestParams, CallToolResult> TouchActivity(
            McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
            (context, cancellationToken) =>
            {
                context.Services?.GetService<IActivityTracker>()?.Touch();

                return next(context, cancellationToken);
            };
    }
}
