namespace Snail.MCP.Figma.Application.Documents;

/// <summary>Brings a variables payload to the one shape its readers understand: sections keyed by id.</summary>
/// <remarks>
/// REST answers with dictionaries keyed by id, the plugin with arrays whose items carry their own id. Read with
/// <c>as JsonObject</c>, a plugin array looked like an empty file: on 19.09.2026 three tools answered
/// "0 collections" with ok true while a collection of eighteen variables existed. A section that is neither
/// shape is refused rather than read as empty, because an empty answer is a claim about the file.
/// </remarks>
public static class VariablesShape
{
    private static readonly string[] Sections = ["variableCollections", "variables"];

    /// <summary>The two sections as dictionaries by id, whichever channel the payload came through.</summary>
    public static JsonObject Keyed(JsonNode source)
    {
        var keyed = new JsonObject();

        foreach (var section in Sections)
        {
            keyed[section] = KeyedSection(source, section);
        }

        return keyed;
    }

    private static JsonObject KeyedSection(JsonNode source, string section) => source[section] switch
    {
        JsonObject byId => (JsonObject)byId.DeepClone(),
        JsonArray items => ById(items, section),
        var other => throw new InvalidOperationException(
            $"The variables payload has no readable '{section}': expected an object keyed by id or an array of items " +
            $"with an id, got {other?.GetValueKind().ToString() ?? "nothing"}. This is a defect of the server or the " +
            "plugin, not an empty file; figma_execute with figma.variables reads the variables meanwhile."),
    };

    private static JsonObject ById(JsonArray items, string section)
    {
        var byId = new JsonObject();

        foreach (var item in items)
        {
            var id = item?["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"An item of '{section}' carries no id, so it cannot be keyed.");

            byId[id] = item!.DeepClone();
        }

        return byId;
    }
}
