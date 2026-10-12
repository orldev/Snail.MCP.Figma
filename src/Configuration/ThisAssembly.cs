using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Configuration;

/// <summary>The name and version the server announces in initialize, computed once at startup.</summary>
internal static class ThisAssembly
{
    public static string Name { get; } = ServerIdentity.NameOf(typeof(ThisAssembly).Assembly);

    public static string InformationalVersion { get; } = ServerIdentity.VersionOf(typeof(ThisAssembly).Assembly);
}
