using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Extensions;

/// <summary>The server as a program: one process per client over stdio, or one shared by every agent of the machine over Streamable HTTP, both with the bridge to the plugin.</summary>
/// <remarks>The standard keeps <c>Program.cs</c> to this one call, so the entry point says that the server runs and the host says how.</remarks>
public static class ServerHost
{
    /// <summary>Builds the host the settings ask for and runs it until it is stopped.</summary>
    public static async Task RunAsync(string[] args)
    {
        ServerFolders.MoveFromSnailNames("figma");

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });
        builder.WebHost.UseKestrelCore();
        builder.Services.AddRoutingCore();

        var config = builder.AddServerConfiguration();

        if (config.IsShared)
        {
            await builder
                .ConfigureSharedMcpServer(config)
                .Build()
                .UseBridge()
                .UseSharedMcp()
                .RunUntilStoppedAsync()
                .ConfigureAwait(false);

            return;
        }

        await builder
            .ConfigureMcpServer(config)
            .Build()
            .UseBridge()
            .RunUntilStoppedAsync()
            .ConfigureAwait(false);
    }
}
