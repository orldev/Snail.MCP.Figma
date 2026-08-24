using System.Text.Json;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Adapters.Editor;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>The plugin lives outside <c>src/</c> as linked Content; breaking the link would silently leave the WRITE channel without a plugin.</summary>
public class EditorExtensionInstallerTests
{
    [Fact]
    public void PluginIsCopiedNextToTheAssembly()
    {
        var pluginDirectory = EditorExtensionInstaller.FindBundledDirectory();

        Assert.NotNull(pluginDirectory);

        foreach (var file in new[] { "code.js", "ui.html", "manifest.json", "icon.png", "LICENSE" })
        {
            Assert.True(File.Exists(Path.Combine(pluginDirectory!, file)), $"missing plugin file: {file}");
        }
    }

    [Fact]
    public void ManifestAllowsTheConfiguredPortRange()
    {
        var pluginDirectory = EditorExtensionInstaller.FindBundledDirectory()!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pluginDirectory, "manifest.json")));

        var allowedDomains = manifest.RootElement
            .GetProperty("networkAccess")
            .GetProperty("allowedDomains")
            .EnumerateArray()
            .Select(d => d.GetString())
            .ToHashSet(StringComparer.Ordinal);

        var config = new Snail.MCP.Figma.Configuration.ServerConfig();

        for (var port = config.Bridge.PortRangeStart; port <= config.Bridge.PortRangeEnd; port++)
        {
            Assert.Contains($"ws://localhost:{port}", allowedDomains);
        }
    }

    [Fact]
    public void PluginVersionIsParsedFromTheInstalledBundle()
    {
        var directory = Directory.CreateTempSubdirectory("snail-version-test");

        try
        {
            var codeJs = Path.Combine(directory.FullName, "code.js");
            File.WriteAllText(codeJs, "// header\nvar PLUGIN_VERSION = '1.39.0-snail.1'; // comment\nrest();");

            Assert.Equal("1.39.0-snail.1", EditorExtensionInstaller.ParsePluginVersion(codeJs));
            Assert.Null(EditorExtensionInstaller.ParsePluginVersion(Path.Combine(directory.FullName, "missing.js")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheBundledCodeJsCarriesAParsableVersion()
    {
        var bundled = EditorExtensionInstaller.FindBundledDirectory();

        Assert.NotNull(bundled);
        Assert.Matches(@"^\d+\.\d+\.\d+", EditorExtensionInstaller.ParsePluginVersion(Path.Combine(bundled!, "code.js"))!);
    }
}
