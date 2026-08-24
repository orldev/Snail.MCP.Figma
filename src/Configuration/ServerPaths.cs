namespace Snail.MCP.Figma.Configuration;

/// <summary>Paths where the server looks for its config and keeps its data.</summary>
public static class ServerPaths
{
    public const string DataDirectoryName = ".snail-mcp-figma";

    public static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    public static string DefaultDataDirectory => Path.Combine(Home, DataDirectoryName);

    /// <summary>Config lookup chain, highest priority first; <c>SNAIL_MCP_FIGMA_CONFIG</c> overrides everything.</summary>
    public static IEnumerable<string> ConfigCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentSettings.ConfigPathVariable);

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv;
        }

        var cwd = Directory.GetCurrentDirectory();

        yield return Path.Combine(cwd, ".snail-mcp-figma.json");

        yield return Path.Combine(cwd, "snail-mcp-figma.json");

        yield return Path.Combine(Home, ".config", "snail-mcp-figma", "config.json");

        yield return Path.Combine(Home, ".snail-mcp-figma.json");
    }

    /// <summary>First existing file from <see cref="ConfigCandidates"/>, or <c>null</c>.</summary>
    public static string? FindConfigFile() => ConfigCandidates().FirstOrDefault(File.Exists);
}
