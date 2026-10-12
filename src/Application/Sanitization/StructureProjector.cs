namespace Snail.MCP.Figma.Application.Sanitization;

/// <summary>Projects the tree onto structural fields: ids, names, types and component links; visuals are dropped.</summary>
public static class StructureProjector
{
    private static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
    {
        "id", "name", "type", "children", "componentId", "componentSetId", "key",
        "pluginData", "sharedPluginData", "componentPropertyDefinitions",
        "layoutMode", "counterAxisSizingMode", "primaryAxisSizingMode",
        "visible", "locked", "isMask", "characters",
    };

    /// <summary>Keeps only structural fields, recursively.</summary>
    public static JsonNode Project(JsonNode source) => source switch
    {
        JsonObject obj => ProjectObject(obj),
        JsonArray array => new JsonArray([.. array.Select(item => item is null ? null : Project(item))]),
        _ => source.DeepClone(),
    };

    private static JsonNode ProjectObject(JsonObject source)
    {
        var result = new JsonObject();

        foreach (var (key, value) in source)
        {
            if (!Keep.Contains(key) || value is null)
            {
                continue;
            }

            result[key] = Project(value);
        }

        return result;
    }
}
