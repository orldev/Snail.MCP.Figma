namespace Snail.MCP.Figma.Tests.Support;

/// <summary>The variables payload exactly as plugin/code.js sends it: arrays whose items carry their own id, never the REST dictionaries.</summary>
/// <remarks>A fake that answered in the REST shape kept the tests green while the real plugin's arrays were read as an empty file; <c>VariablesPayloadContractTests</c> pins the field lists below to the serializers in the bundle.</remarks>
public static class PluginVariables
{
    public static readonly string[] VariableFields =
    [
        "id", "name", "key", "resolvedType", "valuesByMode", "variableCollectionId",
        "scopes", "codeSyntax", "description", "hiddenFromPublishing",
    ];

    public static readonly string[] CollectionFields = ["id", "name", "key", "modes", "defaultModeId", "variableIds"];

    /// <summary>One collection with one colour variable, in the wire shape of VARIABLES_DATA and REFRESH_VARIABLES.</summary>
    public static JsonObject Payload(string variableName = "color/bg") => new()
    {
        ["success"] = true,
        ["timestamp"] = 1_758_240_000_000,
        ["fileKey"] = null,
        ["variables"] = new JsonArray(new JsonObject
        {
            ["id"] = "VariableID:1:2",
            ["name"] = variableName,
            ["key"] = "variable-key",
            ["resolvedType"] = "COLOR",
            ["valuesByMode"] = new JsonObject { ["1:0"] = new JsonObject { ["r"] = 1, ["g"] = 1, ["b"] = 1, ["a"] = 1 } },
            ["variableCollectionId"] = "VariableCollectionId:1:1",
            ["scopes"] = new JsonArray("ALL_SCOPES"),
            ["codeSyntax"] = new JsonObject(),
            ["description"] = "",
            ["hiddenFromPublishing"] = false,
        }),
        ["variableCollections"] = new JsonArray(new JsonObject
        {
            ["id"] = "VariableCollectionId:1:1",
            ["name"] = "Colors",
            ["key"] = "collection-key",
            ["modes"] = new JsonArray(new JsonObject { ["modeId"] = "1:0", ["name"] = "Light" }),
            ["defaultModeId"] = "1:0",
            ["variableIds"] = new JsonArray("VariableID:1:2"),
        }),
    };
}
