namespace Snail.MCP.Figma.Tests;

/// <summary>A setting the server refuses stops the real process before it listens: one line that names the variable, exit code 1, no trace.</summary>
public sealed class StartRefusalTests
{
    /// <summary>The server has no key of its own, so every interface is refused outside a container as inside one.</summary>
    [Fact]
    public async Task Start_OnEveryInterface_IsRefusedInOneLine()
    {
        var dataDirectory = TestFolder.Create();
        var environment = new Dictionary<string, string?>
        {
            ["SNAIL_MCP_FIGMA_DATA_DIRECTORY"] = dataDirectory,
            ["SNAIL_MCP_FIGMA_LISTENS_ON_EVERY_INTERFACE"] = "true",
            ["SNAIL_MCP_FIGMA_ACCESS_TOKEN"] = null,
        };

        try
        {
            var refused = await Assert.ThrowsAsync<ServerExitedException>(() => HttpServerProgram.StartAsync("Snail.MCP.Figma", "SNAIL_FIGMA_SERVER", environment));
            var lines = refused.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("SNAIL_MCP_FIGMA_LISTENS_ON_EVERY_INTERFACE", Assert.Single(lines), StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", refused.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>The settings are bound while the host is registered, before any host could refuse them: a word where the bridge's first port
    /// belongs crashed the process with a trace and exit code 134, and is refused in one line that names the variable. Run against the native
    /// publication, this is the binding source generator's message the refusal reads.</summary>
    [Fact]
    public async Task Start_WithAWordForTheBridgePort_IsRefusedInOneLine_NamingTheVariable()
    {
        var dataDirectory = TestFolder.Create();
        var environment = new Dictionary<string, string?>
        {
            ["SNAIL_MCP_FIGMA_DATA_DIRECTORY"] = dataDirectory,
            ["SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START"] = "abc",
            ["SNAIL_MCP_FIGMA_ACCESS_TOKEN"] = null,
        };

        try
        {
            var refused = await Assert.ThrowsAsync<ServerExitedException>(() => ServerProgram.StartAsync("Snail.MCP.Figma", "SNAIL_FIGMA_SERVER", environment));
            var line = Assert.Single(refused.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            Assert.Equal(1, refused.ExitCode);
            Assert.Contains("SNAIL_MCP_FIGMA_BRIDGE__PORT_RANGE_START", line, StringComparison.Ordinal);
            Assert.Contains("'abc'", line, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", refused.Stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
