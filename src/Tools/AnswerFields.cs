using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Sanitization;

namespace Snail.MCP.Figma.Tools;

/// <summary>The fields the read tools write beside <c>data</c> (<c>ToolAnswer.OkText(data, beside)</c>), mapped from the records the reads return.</summary>
/// <remarks>The names are a contract of these tools: clients and the server's instructions read <c>fileKey</c>, <c>source</c>, <c>fileVersion</c>, <c>staleReason</c> and <c>metrics</c> at the top level of the envelope.</remarks>
public static class AnswerFields
{
    /// <summary>What a read through REST says beside its tree: the file, where the tree came from, its version and what the sanitizer saved.</summary>
    /// <remarks>A stale snapshot says why, since the model would otherwise take it for the file as it is.</remarks>
    public static JsonObject Of(ReadResult read, string fileKey)
    {
        var fields = new JsonObject
        {
            ["fileKey"] = fileKey,
            ["source"] = SourceOf(read),
            ["fileVersion"] = read.FileVersion,
        };

        if (read.IsStale)
        {
            fields["staleReason"] =
                "Figma did not answer the file version request — the snapshot was served from the cache without a check and may be stale";
        }

        fields["metrics"] = Metrics(read.BytesIn, read.BytesOut, read.Ratio);

        return fields;
    }

    /// <summary>What the sanitizer saved on one tree, as the <c>metrics</c> field.</summary>
    public static JsonObject Of(SanitizationResult sanitized) =>
        Metrics(sanitized.BytesIn, sanitized.BytesOut, sanitized.Ratio);

    private static string SourceOf(ReadResult read)
    {
        if (!read.IsFromCache)
        {
            return "figma-api";
        }

        return read.IsStale ? "cache-stale" : "cache";
    }

    private static JsonObject Metrics(long bytesIn, long bytesOut, double ratio) => new()
    {
        ["bytesIn"] = bytesIn,
        ["bytesOut"] = bytesOut,
        ["savedRatio"] = Math.Round(ratio, 4),
    };
}
