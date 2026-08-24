using System.Text.Encodings.Web;
using System.Text.Json;
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
            envelope["source"] = read.FromCache ? (read.IsStale ? "cache-stale" : "cache") : "figma-api";
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
}
