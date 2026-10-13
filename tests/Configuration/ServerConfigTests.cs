using Microsoft.Extensions.Configuration;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Configuration;

public class ServerConfigTests
{
    [Fact]
    public void Defaults_MatchPluginManifestPortRange()
    {
        var config = new ServerConfig();

        Assert.Equal(9223, config.Bridge.PortRangeStart);
        Assert.Equal(9232, config.Bridge.PortRangeEnd);
        Assert.Equal("/", config.Bridge.Path);
    }

    [Fact]
    public void Defaults_UseHomeDataDirectory()
    {
        var config = new ServerConfig();
        var data = ".s3l-mcp-figma";

        Assert.EndsWith(data, config.DataDirectory, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine(data, "snapshots.sqlite"), config.CacheDatabaseFile, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine(data, "plugin"), config.PluginDirectory, StringComparison.Ordinal);
        Assert.EndsWith(Path.Combine(data, "screenshots"), config.ScreenshotDirectory, StringComparison.Ordinal);
        Assert.Equal(30, config.Screenshots.RetainCount);
    }

    [Fact]
    public void Normalize_RepairsInvalidValues()
    {
        var config = new ServerConfig
        {
            Bridge = { PortRangeStart = 9300, PortRangeEnd = 9100, RequestTimeoutSeconds = 0, Path = "ws" },
            Cache = { TtlHours = -1 },
            Sanitization = { DefaultDepth = 0, DefaultMaxNodes = -5 },
            Screenshots = { RetainCount = 0 },
        };

        ServerConfigExtensions.Normalize(config);

        Assert.Equal(9300, config.Bridge.PortRangeEnd);
        Assert.Equal(30, config.Bridge.RequestTimeoutSeconds);
        Assert.Equal("/ws", config.Bridge.Path);
        Assert.Equal(24, config.Cache.TtlHours);
        Assert.Equal(3, config.Sanitization.DefaultDepth);
        Assert.Equal(500, config.Sanitization.DefaultMaxNodes);
        Assert.Equal(30, config.Screenshots.RetainCount);
    }

    [Fact]
    public void Normalize_ExpandsTildeInDataDirectory()
    {
        var config = new ServerConfig { DataDirectory = "~/custom-figma-data" };

        ServerConfigExtensions.Normalize(config);

        Assert.Equal(Path.Combine(ServerPaths.Home, "custom-figma-data"), config.DataDirectory);
    }

    [Theory]
    [InlineData("ACCESS_TOKEN", "AccessToken")]
    [InlineData("MODE", "Mode")]
    [InlineData("IDLE_TIMEOUT_MINUTES", "IdleTimeoutMinutes")]
    [InlineData("SANITIZATION__PROFILE", "Sanitization:Profile")]
    [InlineData("BRIDGE__PREFER_PLUGIN_FOR_READS", "Bridge:PreferPluginForReads")]
    [InlineData("Cache__FreshnessSeconds", "Cache:FreshnessSeconds")]
    public void EnvironmentName_BecomesConfigurationKey(string variable, string expected) =>
        Assert.Equal(expected, EnvironmentSettings.ToConfigurationKey(variable), ignoreCase: true);

    [Fact]
    public void EnvironmentValues_BindIntoConfig_AndBlankOnesAreIgnored()
    {
        var variables = new System.Collections.Hashtable
        {
            ["SNAIL_MCP_FIGMA_ACCESS_TOKEN"] = "  figd_from-env  ",
            ["SNAIL_MCP_FIGMA_SANITIZATION__PROFILE"] = "raw",
            ["SNAIL_MCP_FIGMA_MODE"] = "   ",
            ["PATH"] = "/usr/bin",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(EnvironmentSettings.Read(variables))
            .Build();
        var config = new ServerConfig();
        configuration.Bind(config);
        ServerConfigExtensions.Normalize(config);

        Assert.Equal("figd_from-env", config.AccessToken);
        Assert.Equal(SanitizationProfile.Raw, config.Sanitization.Profile);
        Assert.Equal(ServerMode.Local, config.Mode);
        Assert.Null(configuration["PATH"]);
    }

    [Fact]
    public void ConfigCandidates_PutEnvironmentOverrideFirst()
    {
        var candidates = RunWithEnvironment(
            new Dictionary<string, string?> { ["SNAIL_MCP_FIGMA_CONFIG"] = "/tmp/snail-mcp-figma-explicit.json" },
            () => ServerPaths.ConfigCandidates().ToList());

        Assert.Equal("/tmp/snail-mcp-figma-explicit.json", candidates[0]);
        Assert.Contains(candidates, c => c.EndsWith(".s3l-mcp-figma.json", StringComparison.Ordinal));
        Assert.True(candidates.FindIndex(c => c.EndsWith(".s3l-mcp-figma.json", StringComparison.Ordinal))
            < candidates.FindIndex(c => c.EndsWith(".snail-mcp-figma.json", StringComparison.Ordinal)));
    }

    private static void RunWithEnvironment(Dictionary<string, string?> values, Action action) =>
        RunWithEnvironment(values, () => { action(); return 0; });

    private static T RunWithEnvironment<T>(Dictionary<string, string?> values, Func<T> action)
    {
        var previous = values.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);

        try
        {
            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            return action();
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
