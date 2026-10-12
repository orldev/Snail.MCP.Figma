using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Paths where the server looks for its config and keeps its data.</summary>
public static class ServerPaths
{
    /// <summary>The server's short name, which names its folders: ~/.s3l-mcp-figma and ~/.config/s3l-mcp-figma.</summary>
    public const string ShortName = "figma";

    public static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/.s3l-mcp-figma</c>, moved there from <c>~/.snail-mcp-figma</c> when only that one exists.</summary>
    public static string DefaultDataDirectory => ServerFolders.DataDirectory(ShortName);

    /// <summary>Config lookup chain, highest priority first; <c>SNAIL_MCP_FIGMA_CONFIG</c> overrides everything. The files under the snail
    /// name come after those under the s3l name, for a setup written before the rename.</summary>
    public static IEnumerable<string> ConfigCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentSettings.ConfigPathVariable);

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv;
        }

        var cwd = Directory.GetCurrentDirectory();

        yield return Path.Combine(cwd, $".s3l-mcp-{ShortName}.json");

        yield return Path.Combine(cwd, $"s3l-mcp-{ShortName}.json");

        yield return Path.Combine(ServerFolders.ConfigDirectory(ShortName), "config.json");

        yield return Path.Combine(Home, $".s3l-mcp-{ShortName}.json");

        yield return Path.Combine(cwd, $".snail-mcp-{ShortName}.json");

        yield return Path.Combine(cwd, $"snail-mcp-{ShortName}.json");

        yield return Path.Combine(Home, $".snail-mcp-{ShortName}.json");
    }

    /// <summary>First existing file from <see cref="ConfigCandidates"/>, or <c>null</c>.</summary>
    public static string? FindConfigFile() => ConfigCandidates().FirstOrDefault(File.Exists);
}
