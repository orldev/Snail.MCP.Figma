namespace Snail.MCP.Figma.Application.Documents;

/// <summary>Compacts the Variables API response into a summary plus a flat list.</summary>
public static class VariableFormatter
{
    /// <summary>Builds <c>{collections, variables, summary}</c> from a variables payload of either channel.</summary>
    public static JsonObject Format(JsonNode source, string? namePattern = null)
    {
        var keyed = VariablesShape.Keyed(source);
        var collections = new JsonArray();

        foreach (var (id, collection) in keyed["variableCollections"]!.AsObject())
        {
            collections.Add((JsonNode)new JsonObject
            {
                ["id"] = id,
                ["name"] = collection?["name"]?.DeepClone(),
                ["key"] = collection?["key"]?.DeepClone(),
                ["modes"] = collection?["modes"]?.DeepClone(),
                ["variableCount"] = (collection?["variableIds"] as JsonArray)?.Count ?? 0,
            });
        }

        var variables = new JsonArray();
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (id, variable) in keyed["variables"]!.AsObject())
        {
            var name = variable?["name"]?.GetValue<string>();

            if (namePattern is not null &&
                (name is null || !name.Contains(namePattern, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var resolvedType = variable?["resolvedType"]?.GetValue<string>() ?? "UNKNOWN";
            byType[resolvedType] = byType.GetValueOrDefault(resolvedType) + 1;

            variables.Add((JsonNode)new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["key"] = variable?["key"]?.DeepClone(),
                ["resolvedType"] = resolvedType,
                ["valuesByMode"] = variable?["valuesByMode"]?.DeepClone(),
                ["variableCollectionId"] = variable?["variableCollectionId"]?.DeepClone(),
                ["scopes"] = variable?["scopes"]?.DeepClone(),
                ["description"] = variable?["description"]?.DeepClone(),
            });
        }

        var variablesByType = new JsonObject();

        foreach (var (type, count) in byType.OrderByDescending(pair => pair.Value))
        {
            variablesByType[type] = count;
        }

        return new JsonObject
        {
            ["collections"] = collections,
            ["variables"] = variables,
            ["summary"] = new JsonObject
            {
                ["totalCollections"] = collections.Count,
                ["totalVariables"] = variables.Count,
                ["variablesByType"] = variablesByType,
            },
        };
    }
}
