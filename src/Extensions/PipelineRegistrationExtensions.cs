using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Hosting;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Documents;

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

        /// <summary>Driving adapter: tools are delivered to the model over stdio; every call marks the server as active.</summary>
        public IServiceCollection AddMcpTransport()
        {
            services
                .AddMcpServer()
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
