using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tests.Application.Documents;

/// <summary>An empty variables answer is a claim about the file, so only a payload that was really read may produce one.</summary>
public class VariablesShapeTests
{
    [Fact]
    public void PluginArraysAreKeyedByTheIdEachItemCarries()
    {
        var keyed = VariablesShape.Keyed(PluginVariables.Payload());

        Assert.Equal("Colors", keyed["variableCollections"]!["VariableCollectionId:1:1"]!["name"]!.GetValue<string>());
        Assert.Equal("color/bg", keyed["variables"]!["VariableID:1:2"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void RestDictionariesPassThroughUntouched()
    {
        var rest = new JsonObject
        {
            ["variableCollections"] = new JsonObject { ["C:1"] = new JsonObject { ["name"] = "Core" } },
            ["variables"] = new JsonObject { ["V:1"] = new JsonObject { ["name"] = "space/md" } },
        };

        var keyed = VariablesShape.Keyed(rest);

        Assert.Equal("Core", keyed["variableCollections"]!["C:1"]!["name"]!.GetValue<string>());
        Assert.Equal("space/md", keyed["variables"]!["V:1"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AnEmptyFileIsEmptyInBothShapes()
    {
        var plugin = new JsonObject { ["variableCollections"] = new JsonArray(), ["variables"] = new JsonArray() };
        var rest = new JsonObject { ["variableCollections"] = new JsonObject(), ["variables"] = new JsonObject() };

        Assert.Empty(VariablesShape.Keyed(plugin)["variables"]!.AsObject());
        Assert.Empty(VariablesShape.Keyed(rest)["variables"]!.AsObject());
    }

    [Fact]
    public void APayloadWithoutTheSectionsIsRefusedInsteadOfReadAsEmpty()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => VariablesShape.Keyed(new JsonObject { ["success"] = true }));

        Assert.Contains("not an empty file", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnItemWithoutAnIdIsRefused()
    {
        var payload = new JsonObject
        {
            ["variableCollections"] = new JsonArray(),
            ["variables"] = new JsonArray(new JsonObject { ["name"] = "orphan" }),
        };

        Assert.Throws<InvalidOperationException>(() => VariablesShape.Keyed(payload));
    }
}
