using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Moves plugin screenshots from the MCP response onto disk, replacing <c>base64</c> with a file path.</summary>
public sealed class CaptureStore(ServerConfig config, ILogger<CaptureStore> logger)
    : ICaptureStore
{
    /// <summary>The formats Figma exports, and so the only extensions a capture file may take.</summary>
    /// <remarks>The format arrives in the reply of whatever answered on the bridge; taken verbatim it once named a path, and <c>png/../../x</c> wrote outside the screenshots folder.</remarks>
    private static readonly HashSet<string> ExportFormats = new(["png", "jpg", "jpeg", "svg", "pdf"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes the <c>base64</c> payload of <paramref name="image"/> to a file and returns the same object with a <c>path</c> instead.</summary>
    public JsonObject SaveAndReplace(JsonObject image)
    {
        var base64 = image["base64"]?.GetValue<string>();

        if (string.IsNullOrEmpty(base64))
        {
            throw new InvalidOperationException("The plugin returned no image.base64.");
        }

        var format = image["format"]?.GetValue<string>() ?? "PNG";

        if (!ExportFormats.Contains(format))
        {
            throw new InvalidOperationException($"The plugin returned an image in an unknown format: {format}.");
        }

        var bytes = Convert.FromBase64String(base64);

        Directory.CreateDirectory(config.ScreenshotDirectory);

        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{format.ToLowerInvariant()}";
        var path = Path.Combine(config.ScreenshotDirectory, fileName);
        File.WriteAllBytes(path, bytes);

        image.Remove("base64");
        image["path"] = path;

        Prune();

        return image;
    }

    /// <summary>Keeps only the latest <see cref="ScreenshotOptions.RetainCount"/> files; older ones are deleted right after a write.</summary>
    private void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(config.ScreenshotDirectory)
                .EnumerateFiles()
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(Math.Max(config.Screenshots.RetainCount, 1));

            foreach (var file in stale)
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            Log.ScreenshotsNotPruned(logger, ex, config.ScreenshotDirectory);
        }
    }
}
