using Microsoft.Extensions.Hosting;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Copies the JS plugin from the build output to a stable path under the data directory, where Figma imports its manifest once.</summary>
public sealed class EditorExtensionInstaller(
    ServerConfig config,
    ILogger<EditorExtensionInstaller> logger)
    : IHostedService, IEditorExtensionInstaller
{
    private static readonly string[] RequiredFiles = ["manifest.json", "code.js", "ui.html"];

    /// <summary>Directory whose manifest is imported into Figma.</summary>
    public string TargetDirectory => config.PluginDirectory;

    public string? BundledDirectory => FindBundledDirectory();

    public bool IsInstalled => File.Exists(Path.Combine(TargetDirectory, "manifest.json"));

    /// <summary>Version constant of the installed code.js — what would run after the user restarts the plugin.</summary>
    public string? InstalledPluginVersion => ParsePluginVersion(Path.Combine(TargetDirectory, "code.js"));

    internal static string? ParsePluginVersion(string codeJsPath)
    {
        if (!File.Exists(codeJsPath))
        {
            return null;
        }

        using var reader = new StreamReader(codeJsPath);
        var buffer = new char[2048];
        var read = reader.Read(buffer, 0, buffer.Length);
        var head = new string(buffer, 0, read);
        var match = System.Text.RegularExpressions.Regex.Match(head, "PLUGIN_VERSION = '([^']+)'");

        return match.Success ? match.Groups[1].Value : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Provision();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to install the plugin into {Target}", TargetDirectory);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Copies plugin files, skipping those already up to date.</summary>
    internal int Provision()
    {
        var source = FindBundledDirectory();

        if (source is null)
        {
            logger.LogWarning("No plugin/ directory next to the assembly — the WRITE channel will be unavailable");

            return 0;
        }

        Directory.CreateDirectory(TargetDirectory);
        var copied = 0;

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(TargetDirectory, Path.GetFileName(file));

            if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(file))
            {
                continue;
            }

            File.Copy(file, target, overwrite: true);
            copied++;
        }

        if (copied > 0)
        {
            logger.LogInformation(
                "Plugin updated in {Target} ({Count} files). Import manifest.json in Figma: " +
                "Plugins → Development → Import plugin from manifest",
                TargetDirectory, copied);
        }

        return copied;
    }

    /// <summary>Plugin directory next to the assembly; <c>null</c> when the Content items were not copied.</summary>
    internal static string? FindBundledDirectory()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "plugin");

        return RequiredFiles.All(file => File.Exists(Path.Combine(candidate, file))) ? candidate : null;
    }
}
