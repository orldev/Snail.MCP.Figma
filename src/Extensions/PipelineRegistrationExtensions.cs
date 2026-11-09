using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Libraries;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Hosting;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Discovery;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Use cases: the READ pipeline (read → sanitize → render), snapshot policy and the MCP transport.</summary>
public static class PipelineRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the read pipeline: snapshot invalidation, version history and diffs, sanitizing, reading and rendering.</summary>
        public IServiceCollection AddReadPipeline()
        {
            services.AddSingleton<SnapshotInvalidator>();
            services.AddSingleton(provider => new VersionSnapshotCache(
                provider.GetRequiredService<CacheOptions>().VersionSnapshotEntries));
            services.AddSingleton<VersionSnapshots>();
            services.AddSingleton<VersionLog>();
            services.AddSingleton<VersionComparison>();
            services.AddSingleton<NodeBlame>();
            services.AddSingleton<KitAssembly>();
            services.AddSingleton<PublishedLibrary>();
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

        /// <summary>Driving adapter over Streamable HTTP: every client keeps a session, and every session is an agent of its own.</summary>
        /// <remarks>
        /// <para>Every request must belong to a session, which <c>WithLocalHttpTransport</c> of Snail.Sdk requires: the 2026-07-28 revision has none and is refused (-32022), so every Claude Code window falls back to initialize and becomes an agent of its own, with its folder.</para>
        /// <para>A session nobody has used for <see cref="SessionIdleTimeout"/> ends, and its agent leaves with it: a VS Code window that was killed never says goodbye, and its agent would otherwise stay in the panel of the file it worked in.</para>
        /// <para>The SDK marks sessions obsolete (MCP9006), kept for clients of the 2025 revisions, which every agent's client still speaks; and the session runner experimental (MCPEXP002), the one place that sees a session end.</para>
        /// </remarks>
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
        /// <remarks>
        /// <para>Each tool class is named here rather than found by scanning the assembly, which Native AOT cannot do; the stdio contract fails when a class is left out, because it compares tools/list with every tool of the assembly.</para>
        /// <para>Snail.Sdk's <c>WithSessionGrant</c> holds a session to the tools its address names (<c>?tool=</c>, written by the platform that opens the server for a role): the others are left out of its list and refused in the envelope, type <c>notGranted</c>. An address that names none, and stdio, which has no address, are granted every tool, so a Claude Code window on the shared server sees all 141.</para>
        /// <para>Snail.Sdk's <c>ExplainFailures</c> flags an answer that is the failure envelope as an error: 136 of the 141 tools return the envelope as a string, which the MCP SDK turns into a result without isError. <c>WithArgumentCheck</c> answers a call that leaves out a required argument, or sends one the tool does not take, with the envelope of type <c>arguments</c> before the tool runs — five calls of the shared server's log of 16–27.10.2026 reached the SDK's marshaller without a required argument and got "An error occurred invoking" with no cause.</para>
        /// </remarks>
        private IMcpServerBuilder AddMcpServerWithTools()
        {
            var json = ToolJson.Options(ToolJsonContext.Default);

            return services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = ServerIdentity.Of(typeof(PipelineRegistrationExtensions).Assembly);
                    options.ServerInstructions = ServerInstructions.Text;
                })
                .WithTools<CommentTools>(json)
                .WithTools<ComponentTools>(json)
                .WithTools<DesignCodeTools>(json)
                .WithTools<DesignSystemExtractionTools>(json)
                .WithTools<DesignSystemKitTools>(json)
                .WithTools<DiagnosticsTools>(json)
                .WithTools<FigJamTools>(json)
                .WithTools<LibraryTools>(json)
                .WithTools<NodeTools>(json)
                .WithTools<QualityTools>(json)
                .WithTools<ReadTools>(json)
                .WithTools<SessionTools>(json)
                .WithTools<SlidesTools>(json)
                .WithTools<SlotTools>(json)
                .WithTools<TokenTools>(json)
                .WithTools<VariableTools>(json)
                .WithTools<VersionTools>(json)
                .WithTools<WriteTools>(json)
                .ExplainFailures()
                .WithSessionGrant()
                .WithArgumentCheck()
                .WithRequestFilters(filters => filters.AddCallToolFilter(ServeTheAgent));
        }
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

            if (agents.IsShared)
            {
                var server = context.Server;
                var logger = context.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ClientFolder));

                await agent.NameOnceAsync(async () =>
                {
                    if (await ClientFolder.NameAsync(server, logger, CancellationToken.None) is { } folder)
                    {
                        await agents.NameAsync(agent, folder);
                    }
                }).WaitAsync(cancellationToken);
            }

            return await next(context, cancellationToken);
        };
}
