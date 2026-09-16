using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tools;

/// <summary>Reads the <c>deliver</c> parameter and decides whether a picture rides inside the tool result.</summary>
/// <remarks>A path is useless to a caller in another process, and base64 in every answer is useless to a human working interactively — so the caller says which it wants, and the default stays what it always was.</remarks>
internal static class PictureDelivery
{
    /// <summary>Only the file path, the way this server has always answered.</summary>
    internal const string PathOnly = "path";

    /// <summary>The file path and the picture itself.</summary>
    internal const string Both = "both";

    /// <summary>What to say when the value is not one of the two.</summary>
    internal const string Hint = "Expected deliver=\"path\" (the default) or deliver=\"both\".";

    /// <summary>Reads the parameter.</summary>
    /// <remarks>An unknown value is refused rather than quietly read as the default: a typo would otherwise cost the caller the picture in silence.</remarks>
    internal static bool TryParse(string? deliver, out bool inline, out string? error)
    {
        var value = string.IsNullOrWhiteSpace(deliver) ? PathOnly : deliver.Trim().ToLowerInvariant();

        inline = value == Both;
        error = value is PathOnly or Both ? null : $"Unknown deliver '{deliver}'.";

        return error is null;
    }

    /// <summary>The picture to attach, or nothing when the caller did not ask, the format is not one a client renders, or it is too big to carry.</summary>
    /// <remarks>Must be called BEFORE the capture store replaces the base64 with a path — that is the moment the bytes are still in hand.</remarks>
    internal static Picture? From(JsonObject? picture, bool inline)
    {
        if (!inline || picture?["base64"]?.GetValue<string>() is not { Length: > 0 } base64)
        {
            return null;
        }

        var bytes = base64.Length / 4L * 3L;

        return ImageLimits.MimeFor(picture["format"]?.GetValue<string>()) is { } mime && bytes <= ImageLimits.MaxInlineBytes
            ? new Picture(base64, mime)
            : null;
    }
}
