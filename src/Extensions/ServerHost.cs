using Microsoft.Extensions.Configuration;
using Snail.MCP.Figma.Configuration;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Figma.Extensions;

/// <summary>The server as a program: one process per client over stdio, or one shared by every agent of the machine over Streamable HTTP, both with the bridge to the plugin.</summary>
/// <remarks>The standard keeps <c>Program.cs</c> to this one call, so the entry point says that the server runs and the host says how.</remarks>
public static class ServerHost
{
    /// <summary>Builds the host the settings ask for and runs it until it is stopped.</summary>
    /// <remarks>
    /// <para>The settings are read once, before a host exists, since the transport is decided from them (Snail.Sdk's <see cref="McpEndpoint"/>, the <c>Snail</c> section read as well); either host is built over them rather than reading them again.</para>
    /// <para>The stdio host is Snail.Sdk's web host without an MCP endpoint (<see cref="McpHttpHost.CreateStdioBuilder"/>), since the bridge to the plugin is a Kestrel endpoint of the same process.</para>
    /// </remarks>
    public static async Task RunAsync(string[] args)
    {
        var settings = ServerSettings.Read(ServerPaths.ShortName);
        var endpoint = ReadEndpoint(settings);

        if (endpoint.Transport is McpTransport.Http)
        {
            await McpHttpHost.CreateBuilder(args, endpoint, settings)
                .ConfigureSharedMcpServer(endpoint)
                .Build()
                .UseBridge()
                .UseSharedMcp()
                .RunUntilStoppedAsync()
                .ConfigureAwait(false);

            return;
        }

        await McpHttpHost.CreateStdioBuilder(args, settings)
            .ConfigureMcpServer(endpoint)
            .Build()
            .UseBridge()
            .RunUntilStoppedAsync()
            .ConfigureAwait(false);
    }

    /// <summary>Where the server answers, read from its settings: stdio, or Streamable HTTP at an address on loopback.</summary>
    /// <remarks>The server has no key of its own: whoever reaches its MCP port drives Figma as the human. So it keeps to loopback (<see cref="McpReach.LoopbackOnly"/>), and an address beyond it, every interface or a start inside a container is refused here, naming the variable that asked for it.</remarks>
    /// <exception cref="StartRefusedException">The settings name an address the server may not listen on, or one it cannot mean; the refusal names the variable by the server's name, in a test's process too.</exception>
    internal static McpEndpoint ReadEndpoint(IConfiguration settings)
    {
        var options = new McpEndpointOptions
        {
            FallbackSection = ServerConfig.SectionName,
            Reach = McpReach.LoopbackOnly,
            Server = ServerPaths.ShortName,
        };

        return McpEndpoint.Read(settings, ServerConfig.DefaultUrl, options);
    }
}
