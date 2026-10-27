using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Snapshots;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Hosting;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.Sdk.Mcp.Hosting;
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
        /// <remarks>Each tool class is named here rather than found by scanning the assembly, which Native AOT cannot do; the stdio contract fails when a class is left out, because it compares tools/list with every tool of the assembly.</remarks>
        private IMcpServerBuilder AddMcpServerWithTools()
        {
            var json = ToolJson.Options(ToolJsonContext.Default);

            return services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = ServerIdentity.Of(typeof(ThisAssembly).Assembly);
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
                .WithRequestFilters(filters => filters
                    .AddCallToolFilter(TouchActivity)
                    .AddCallToolFilter(RefuseIncompleteCalls)
                    .AddCallToolFilter(ServeTheAgent));
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

    /// <summary>Answers a call that lacks a required argument with the error envelope every tool answers with, naming what is missing.</summary>
    /// <remarks>Without this the SDK's marshaller threw ArgumentException past every tool's guard, and the agent got "An error occurred invoking" with no cause and no hint: five such calls in the shared server's log of 16–27.10.2026, four of them figma_navigate without fileUrlOrKey.</remarks>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> RefuseIncompleteCalls(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is not McpServerTool tool)
            {
                return next(context, cancellationToken);
            }

            var missing = ToolArguments.Missing(tool.ProtocolTool.InputSchema, context.Params?.Arguments);

            if (missing.Count == 0)
            {
                return next(context, cancellationToken);
            }

            var names = string.Join(", ", missing);

            if (context.Services?.GetService<ILoggerFactory>() is { } loggers)
            {
                Log.CalledWithoutRequiredArguments(loggers.CreateLogger(typeof(ToolArguments)), tool.ProtocolTool.Name, names);
            }

            var answer = ToolResponse.Textual(ToolResponse.Error(
                $"{tool.ProtocolTool.Name} was called without the required argument(s) {names}.",
                Messages.PassRequiredArgumentsHint));
            answer.IsError = true;

            return ValueTask.FromResult(answer);
        };

    private static McpRequestHandler<CallToolRequestParams, CallToolResult> TouchActivity(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (context, cancellationToken) =>
        {
            context.Services?.GetService<IActivityTracker>()?.Touch();

            return next(context, cancellationToken);
        };
}
