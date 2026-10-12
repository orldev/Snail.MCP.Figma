using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tools;

/// <summary>Uniform tool response: payload plus savings metrics.</summary>
public static class ToolResponse
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Successful response.</summary>
    public static string Ok(JsonNode data, Action<JsonObject>? decorate = null)
    {
        var envelope = new JsonObject { ["ok"] = true, ["data"] = data };
        decorate?.Invoke(envelope);

        return envelope.ToJsonString(Options);
    }

    /// <summary>Response for a read snapshot: adds the source and byte savings.</summary>
    public static string FromRead(ReadResult read, string fileKey, Action<JsonObject>? decorate = null) =>
        Ok(read.Node, envelope =>
        {
            envelope["fileKey"] = fileKey;
            envelope["source"] = read.IsFromCache ? (read.IsStale ? "cache-stale" : "cache") : "figma-api";
            envelope["fileVersion"] = read.FileVersion;

            if (read.IsStale)
            {
                envelope["staleReason"] =
                    "Figma did not answer the file version request — the snapshot was served from the cache without a check and may be stale";
            }

            envelope["metrics"] = new JsonObject
            {
                ["bytesIn"] = read.BytesIn,
                ["bytesOut"] = read.BytesOut,
                ["savedRatio"] = Math.Round(read.Ratio, 4),
            };
            decorate?.Invoke(envelope);
        });

    /// <summary>Error in a shape the model can act on; <paramref name="data"/> carries per-item breakdowns of partially failed batches.</summary>
    public static string Error(string message, string? hint = null, JsonNode? data = null)
    {
        var envelope = new JsonObject
        {
            ["ok"] = false,
            ["error"] = message,
            ["hint"] = hint,
        };

        if (data is not null)
        {
            envelope["data"] = data;
        }

        return envelope.ToJsonString(Options);
    }

    /// <summary>Serializes any node as compact JSON.</summary>
    public static string Serialize(JsonNode node) => node.ToJsonString(Options);

    /// <summary>The success envelope twice: as the text every client reads, and as structured content for the clients that validate it against the tool's output schema.</summary>
    public static CallToolResult Structured(JsonNode data)
    {
        var envelope = new JsonObject { ["ok"] = true, ["data"] = data }.ToJsonString(Options);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = envelope }],
            StructuredContent = JsonSerializer.Deserialize<JsonElement>(envelope),
        };
    }

    /// <summary>The envelope as the single text block.</summary>
    /// <remarks>This is the shape of every tool that cannot answer with a picture, and of a picture tool asked for a path.</remarks>
    public static CallToolResult Textual(string envelope) => new()
    {
        Content = [new TextContentBlock { Text = envelope }],
    };

    /// <summary>The envelope plus the picture itself, so a caller in another process sees the frame instead of a path it cannot open.</summary>
    /// <remarks><see cref="ImageContentBlock.Data"/> carries the base64 TEXT as UTF-8 bytes, not the decoded picture: handing it the decoded bytes puts raw PNG on the wire, which the client rejects as invalid base64. Without a picture the result is the plain text one.</remarks>
    public static CallToolResult Pictured(string envelope, Picture? picture) =>
        picture is { } shown
            ? new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = envelope },
                    new ImageContentBlock { Data = Encoding.UTF8.GetBytes(shown.Base64), MimeType = shown.MimeType },
                ],
            }
            : Textual(envelope);
}
