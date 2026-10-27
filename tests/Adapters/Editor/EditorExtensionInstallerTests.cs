using System.Text.Json;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>The plugin lives outside <c>src/</c> as linked Content; breaking the link would silently leave the WRITE channel without a plugin.</summary>
public class EditorExtensionInstallerTests
{
    [Fact]
    public void EditorExtensionInstaller_BundledPlugin_LiesNextToTheAssembly()
    {
        var pluginDirectory = EditorExtensionInstaller.FindBundledDirectory();

        Assert.NotNull(pluginDirectory);

        foreach (var file in new[] { "code.js", "ui.html", "manifest.json", "icon.png", "LICENSE" })
        {
            Assert.True(File.Exists(Path.Combine(pluginDirectory!, file)), $"missing plugin file: {file}");
        }
    }

    [Fact]
    public void EditorExtensionInstaller_BundledManifest_AllowsTheConfiguredPortRange()
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
    public void EditorExtensionInstaller_InstalledBundle_YieldsItsPluginVersion()
    {
        var directory = TestFolder.Create();
        var codeJs = Path.Combine(directory, "code.js");
        File.WriteAllText(codeJs, "// header\nvar PLUGIN_VERSION = '1.39.0-snail.1'; // comment\nrest();");

        Assert.Equal("1.39.0-snail.1", EditorExtensionInstaller.ParsePluginVersion(codeJs));
        Assert.Null(EditorExtensionInstaller.ParsePluginVersion(Path.Combine(directory, "missing.js")));
    }

    [Fact]
    public void EditorExtensionInstaller_BundledCodeJs_CarriesAParsableVersion()
    {
        var bundled = EditorExtensionInstaller.FindBundledDirectory();

        Assert.NotNull(bundled);
        Assert.Matches(@"^\d+\.\d+\.\d+", EditorExtensionInstaller.ParsePluginVersion(Path.Combine(bundled!, "code.js"))!);
    }

    /// <summary>The installed panel carries the machine's bridge token, or the hub refuses it as a page posing as the plugin.</summary>
    [Fact]
    public void EditorExtensionInstaller_Provisioned_WritesTheTokenIntoThePanel()
    {
        var token = new BridgeToken(new string('a', 64));
        var installer = new EditorExtensionInstaller(new ServerConfig { DataDirectory = TestFolder.Create() }, token, NullLogger<EditorExtensionInstaller>.Instance);

        installer.Provision();

        var panel = File.ReadAllText(Path.Combine(installer.TargetDirectory, "ui.html"));
        Assert.Contains(token.Value, panel, StringComparison.Ordinal);
        Assert.DoesNotContain(BridgeToken.Placeholder, panel, StringComparison.Ordinal);
    }

    /// <summary>A token made anew — its file deleted — must reach a panel already installed and newer than the bundle, or every panel stays refused.</summary>
    [Fact]
    public void EditorExtensionInstaller_TokenMadeAnew_RewritesTheInstalledPanel()
    {
        var config = new ServerConfig { DataDirectory = TestFolder.Create() };
        new EditorExtensionInstaller(config, new BridgeToken(new string('a', 64)), NullLogger<EditorExtensionInstaller>.Instance).Provision();
        var renewed = new BridgeToken(new string('b', 64));

        new EditorExtensionInstaller(config, renewed, NullLogger<EditorExtensionInstaller>.Instance).Provision();

        Assert.Contains(renewed.Value, File.ReadAllText(Path.Combine(config.PluginDirectory, "ui.html")), StringComparison.Ordinal);
    }

    /// <summary>Two servers starting at once must end up with one token between them, or one of them refuses every panel.</summary>
    [Fact]
    public async Task BridgeToken_TwoServersStartTogether_AgreeOnOneToken()
    {
        var dataDirectory = TestFolder.Create();

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => BridgeToken.LoadOrCreate(dataDirectory).Value)));

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));
    }
}
