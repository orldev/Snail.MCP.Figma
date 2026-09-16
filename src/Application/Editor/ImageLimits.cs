namespace Snail.MCP.Figma.Application.Editor;

/// <summary>Caps on pictures the server reads from disk for the editor.</summary>
/// <remarks>The bridge relays image bytes through the plugin window as a plain number array, so an oversized file stalls that window instead of failing outright; the cap turns it into an answer the model can act on.</remarks>
public static class ImageLimits
{
    /// <summary>Largest picture file read from disk, in megabytes.</summary>
    /// <remarks><c>ToolCatalogConventionTests</c> keeps the tool descriptions on this number.</remarks>
    public const int MaxFileMegabytes = 10;

    /// <summary>The same cap in bytes.</summary>
    public const long MaxFileBytes = MaxFileMegabytes * 1024L * 1024L;

    /// <summary>Longest side Figma accepts.</summary>
    /// <remarks>Beyond it <c>createImage</c> fails inside the plugin as "Image is too large" — worth refusing earlier, with the real size named.</remarks>
    public const int MaxPixelSide = 4096;

    /// <summary>Extensions Figma accepts as a raster fill.</summary>
    public static readonly string[] RasterExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>Largest picture, in megabytes, worth carrying inside a tool result.</summary>
    /// <remarks>Base64 adds about a third on top of it, and a page captured at 2x passes this easily.</remarks>
    public const int MaxInlineMegabytes = 2;

    /// <summary>The same ceiling in bytes.</summary>
    public const long MaxInlineBytes = MaxInlineMegabytes * 1024L * 1024L;

    /// <summary>MIME type of a picture a client can render, or <c>null</c> when the format is not one.</summary>
    /// <remarks>SVG is markup and PDF is a document: neither belongs in an image block.</remarks>
    public static string? MimeFor(string? format) => format?.Trim().ToUpperInvariant() switch
    {
        "PNG" => "image/png",
        "JPG" or "JPEG" => "image/jpeg",
        _ => null,
    };
}
