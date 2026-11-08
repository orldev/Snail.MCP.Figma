using ModelContextProtocol.Protocol;
using Snail.MCP.Figma.Application.Editor;
using Snail.Sdk.Mcp.Hosting.Answers;

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

    /// <summary>A success of a picture tool: the data in the envelope, and the picture after it in a block of its own when there is one.</summary>
    internal static CallToolResult Answer(JsonNode data, Picture? picture = null) =>
        Attach(ToolAnswer.Ok(data, AnswerShape.None), picture);

    /// <summary>A failure the read tools share, written already as their string answer (<see cref="ToolAnswer.FailedText"/>), as the answer of a picture tool.</summary>
    /// <remarks>The text is the envelope; <c>ExplainFailures</c> flags it isError on its way out.</remarks>
    internal static CallToolResult Answer(string envelope) =>
        new() { Content = [new TextContentBlock { Text = envelope }] };

    /// <summary>A result of a picture tool — a render with its file and source beside the data (<see cref="ToolAnswer.Ok(JsonNode, AnswerShape, JsonObject, string)"/>) — with the picture after it when there is one.</summary>
    internal static CallToolResult Attach(CallToolResult answer, Picture? picture) =>
        picture is { } shown ? answer.WithImage(shown.Bytes, shown.MimeType) : answer;

    /// <summary>The picture to attach, or nothing when the caller did not ask, the format is not one a client renders, or it is too big to carry.</summary>
    /// <remarks>Must be called BEFORE the capture store replaces the base64 with a path — that is the moment the bytes are still in hand.</remarks>
    internal static Picture? From(JsonObject? picture, bool inline)
    {
        if (!inline || picture?["base64"]?.GetValue<string>() is not { Length: > 0 } base64)
        {
            return null;
        }

        var size = base64.Length / 4L * 3L;

        if (ImageLimits.MimeFor(picture["format"]?.GetValue<string>()) is not { } mime || size > ImageLimits.MaxInlineBytes)
        {
            return null;
        }

        var bytes = new byte[size];

        return Convert.TryFromBase64String(base64, bytes, out var written) && written > 0
            ? new Picture(bytes[..written], mime)
            : null;
    }
}
