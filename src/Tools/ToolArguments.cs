using System.Text.Json;

namespace Snail.MCP.Figma.Tools;

/// <summary>What a call must carry, read from the tool's own input schema rather than from the method it reaches.</summary>
public static class ToolArguments
{
    /// <summary>The required arguments the call does not carry, in the order the schema names them; empty when the call is complete or the schema requires nothing.</summary>
    /// <remarks>Only presence is checked: a value of the wrong shape is the marshaller's to refuse, with the parameter it could not bind.</remarks>
    public static IReadOnlyList<string> Missing(JsonElement inputSchema, IDictionary<string, JsonElement>? arguments)
    {
        if (inputSchema.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!inputSchema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var missing = new List<string>();

        foreach (var entry in required.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = entry.GetString()!;

            if (arguments is null || !arguments.ContainsKey(name))
            {
                missing.Add(name);
            }
        }

        return missing;
    }
}
