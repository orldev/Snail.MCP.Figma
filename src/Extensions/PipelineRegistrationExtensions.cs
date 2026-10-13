using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Hosting;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Use cases: the READ pipeline (read → sanitize → render), snapshot policy and the MCP transport.</summary>
public static class PipelineRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddReadPipeline()
        {
            services.AddSingleton<SnapshotInvalidator>();
            services.AddSingleton(provider => new VersionSnapshotCache(
                provider.GetRequiredService<CacheOptions>().VersionSnapshotEntries));
            services.AddSingleton<NodeSanitizer>();
            services.AddSingleton<DocumentReader>();
            services.AddSingleton<DocumentRenderer>();

            return services;
        }

        /// <summary>Driving adapter over stdio: the client that started the process is its one agent.</summary>
        public IServiceCollection AddMcpTransport()
        {
            services.AddMcpServerWithTools().WithStdioServerTransport();

            return services;
        }

        /// <summary>Driving adapter over Streamable HTTP: every client that opens with initialize keeps a session, and every session is an agent of its own.</summary>
        /// <remarks>A session nobody has used for <see cref="SessionIdleTimeout"/> ends, and its agent leaves with it: a VS Code window that was killed never says goodbye, and its agent would otherwise stay in the panel of the file it worked in.
        /// <para>The SDK marks the session timeout obsolete (MCP9006) because sessions are kept only for clients of the 2025 protocol revisions, which is every agent's client today, and the session runner experimental (MCPEXP002); it is the one place that sees a session end.</para></remarks>
        public IServiceCollection AddMcpHttpTransport()
        {
            services.AddMcpServerWithTools().WithLocalHttpTransport();
            services.Configure<HttpServerTransportOptions>(options =>
            {
#pragma warning disable MCP9006, MCPEXP002
                options.IdleTimeout = SessionIdleTimeout;
                options.RunSessionHandler = RunTheAgentsSessionAsync;
#pragma warning restore MCP9006, MCPEXP002
            });

            return services;
        }

        /// <summary>The one server both transports carry: its name, the instructions on the handshake, every tool, and the filters every call passes.</summary>
        private IMcpServerBuilder AddMcpServerWithTools() =>
            services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = ServerIdentity.Of(typeof(ThisAssembly).Assembly);
                    options.ServerInstructions = ServerInstructions.Text;
                })
                .WithToolsFromAssembly()
                .WithRequestFilters(filters => filters
                    .AddCallToolFilter(TouchActivity)
                    .AddCallToolFilter(ServeTheAgent));
    }

    /// <summary>How long a session may go without a request before it ends.</summary>
    public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Runs a session and lets its agent go once it ends, whether the client closed it or it timed out.</summary>
    private static async Task RunTheAgentsSessionAsync(HttpContext context, McpServer server, CancellationToken cancellationToken)
    {
        var agents = context.RequestServices.GetService<AgentRoster>();

        try
        {
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (agents is not null && server.SessionId is { } session)
            {
                await agents.CloseAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Serves the call as the agent of its MCP session for as long as it runs: the tools built for it see that agent's file, remarks and console. On a shared server the agent's first call asks the client for its folder, which names the agent in the panel.</summary>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> ServeTheAgent(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.Services?.GetService<AgentRoster>() is not { } agents)
            {
                return await next(context, cancellationToken);
            }

            using var serving = agents.Serve(context.Server.SessionId, context.Server.ClientInfo?.Name);
            var agent = agents.Current;

            if (agents.IsShared && agent.IsFirstToAskForTheFolder() && await ClientFolder.NameAsync(context.Server, cancellationToken) is { } folder)
            {
                await agents.NameAsync(agent, folder);
            }

            return await next(context, cancellationToken);
        };

    private static McpRequestHandler<CallToolRequestParams, CallToolResult> TouchActivity(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            context.Services?.GetService<IActivityTracker>()?.Touch();

            return next(context, cancellationToken);
        };
}
