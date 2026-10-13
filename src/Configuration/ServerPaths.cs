using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Where the server keeps its data; its settings file is Snail.Sdk's <see cref="ServerSettings"/>.</summary>
public static class ServerPaths
{
    /// <summary>The server's short name, which names its folders: ~/.s3l-mcp-figma and ~/.config/s3l-mcp-figma.</summary>
    public const string ShortName = "figma";

    public static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/.s3l-mcp-figma</c>, moved there from <c>~/.snail-mcp-figma</c> when only that one exists.</summary>
    public static string DefaultDataDirectory => ServerFolders.DataDirectory(ShortName);
}
