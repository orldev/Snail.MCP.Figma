namespace Snail.MCP.Figma.Application.Libraries;

/// <summary>The components the open file offers, as the plugin reads them: its own sets and components, and the library components its instances use.</summary>
/// <remarks>A library component in use is the one part of a library the plugin can see: an instance placed in the file knows its main component, and that carries the key it is imported by. Instances nested inside other instances are the inside of a component and are not asked.</remarks>
public static class OpenFileComponents
{
    /// <summary>What the plugin is asked for: its local components, and the library components behind the instances.</summary>
    public static JsonObject Request() => new() { ["includeLibrary"] = true };

    /// <summary>How long the plugin may take: it walks every page and then asks every instance for its main component.</summary>
    /// <remarks>Apple's iOS 27 kit, 46 pages with 216 sets, takes 10 to 25 seconds, past the 30-second default of a command once a file holds a copy of it. The panel itself waits 300 seconds.</remarks>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(150);

    /// <summary>The answer of the plugin cut to the matches of <paramref name="query"/>, at most <paramref name="limit"/> of each kind.</summary>
    /// <param name="answer">The plugin's GET_LOCAL_COMPONENTS answer, as the panel relays it: <c>{ success, data }</c>.</param>
    /// <param name="query">A substring of the name, or of the set a library variant belongs to; empty matches everything.</param>
    /// <param name="limit">How many local components and how many library components at most.</param>
    /// <exception cref="EditorCommandException">The plugin answered that it could not list the components.</exception>
    public static JsonObject Match(JsonNode? answer, string? query, int limit)
    {
        if (answer?["success"] is JsonValue success && success.TryGetValue<bool>(out var succeeded) && !succeeded)
        {
            throw new EditorCommandException($"The plugin could not list the components: {answer["error"]?.ToString() ?? "no reason given"}");
        }

        answer = answer?["data"] ?? answer;
        var local = Local(answer);
        var library = answer?["libraryComponents"] as JsonArray ?? [];
        var matchedLocal = local.Where(component => Contains(component?["name"], query)).Take(limit).ToList();
        var matchedLibrary = library
            .Where(component => Contains(component?["name"], query) || Contains(component?["set"]?["name"], query))
            .Take(limit)
            .Select(component => component?.DeepClone())
            .ToList();

        var data = new JsonObject
        {
            ["source"] = "open file",
            ["components"] = new JsonArray([.. matchedLocal]),
            ["returned"] = matchedLocal.Count,
            ["totalInFile"] = local.Count,
            ["library"] = new JsonArray([.. matchedLibrary]),
            ["libraryInFile"] = library.Count,
        };

        if (answer?["libraryInstancesTotal"] is JsonValue total && answer["libraryInstancesScanned"] is JsonValue scanned
            && total.GetValue<int>() > scanned.GetValue<int>())
        {
            data["libraryInstancesScanned"] = scanned.GetValue<int>();
            data["libraryInstancesTotal"] = total.GetValue<int>();
        }

        return data;
    }

    /// <summary>The file's own sets first, each with its variants cut to key and name, then its standalone components.</summary>
    private static List<JsonNode?> Local(JsonNode? answer)
    {
        var local = new List<JsonNode?>();

        foreach (var set in answer?["componentSets"] as JsonArray ?? [])
        {
            if (set?.DeepClone() is not JsonObject compact)
            {
                continue;
            }

            if (compact["variants"] is JsonArray variants)
            {
                compact["variants"] = new JsonArray([.. variants.Select(variant => (JsonNode)new JsonObject
                {
                    ["key"] = variant?["key"]?.DeepClone(),
                    ["name"] = variant?["name"]?.DeepClone(),
                })]);
            }

            compact.Remove("defaultVariant");
            local.Add(compact);
        }

        foreach (var component in (answer?["components"] ?? answer) as JsonArray ?? [])
        {
            local.Add(component?.DeepClone());
        }

        return local;
    }

    private static bool Contains(JsonNode? text, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || (text is JsonValue value && value.TryGetValue<string>(out var name) && name.Contains(query, StringComparison.OrdinalIgnoreCase));
}
