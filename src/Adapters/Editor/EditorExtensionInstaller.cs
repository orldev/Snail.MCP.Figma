using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Copies the JS plugin from the build output to a stable path under the data directory, where Figma imports its manifest once.</summary>
public sealed partial class EditorExtensionInstaller(
    ServerConfig config,
    BridgeToken token,
    ILogger<EditorExtensionInstaller> logger)
    : IHostedService, IEditorExtensionInstaller
{
    private static readonly string[] RequiredFiles = ["manifest.json", "code.js", "ui.html"];

    /// <summary>The file the machine's bridge token is written into.</summary>
    private const string StampedFile = "ui.html";

    /// <summary>Directory whose manifest is imported into Figma.</summary>
    public string TargetDirectory => config.PluginDirectory;

    /// <inheritdoc/>
    public string? BundledDirectory => FindBundledDirectory();

    /// <summary>Whether a <c>manifest.json</c> already stands in <see cref="TargetDirectory"/> for Figma to import.</summary>
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
        var match = PluginVersionPattern().Match(head);

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
            Log.PluginNotInstalled(logger, ex, TargetDirectory);
        }

        return Task.CompletedTask;
    }

    /// <summary>Does nothing: the installed plugin stays on disk when the server stops.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Copies plugin files, skipping those already up to date, and writes the machine's bridge token into the panel.</summary>
    /// <remarks>An installed panel without the current token is written again even when it is newer: otherwise a token made anew would leave every panel refused.</remarks>
    internal int Provision()
    {
        var source = FindBundledDirectory();

        if (source is null)
        {
            Log.PluginNotBundled(logger);

            return 0;
        }

        Directory.CreateDirectory(TargetDirectory);
        var copied = 0;

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(TargetDirectory, Path.GetFileName(file));
            var isStamped = string.Equals(Path.GetFileName(file), StampedFile, StringComparison.Ordinal);
            var isCurrent = File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(file);

            if (isCurrent && (!isStamped || File.ReadAllText(target).Contains(token.Value, StringComparison.Ordinal)))
            {
                continue;
            }

            if (isStamped)
            {
                File.WriteAllText(target, File.ReadAllText(file).Replace(BridgeToken.Placeholder, token.Value, StringComparison.Ordinal));
            }
            else
            {
                File.Copy(file, target, overwrite: true);
            }

            copied++;
        }

        if (copied > 0)
        {
            Log.PluginUpdated(logger, TargetDirectory, copied);
        }

        return copied;
    }

    /// <summary>Plugin directory next to the assembly; <c>null</c> when the Content items were not copied.</summary>
    internal static string? FindBundledDirectory()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "plugin");

        return RequiredFiles.All(file => File.Exists(Path.Combine(candidate, file))) ? candidate : null;
    }

    [GeneratedRegex("PLUGIN_VERSION = '([^']+)'")]
    private static partial Regex PluginVersionPattern();
}
