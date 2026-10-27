using System.Text.Json;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>The required arguments of a call, read from the tool's input schema.</summary>
public class ToolArgumentsTests
{
    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private static Dictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    [Fact]
    public void Missing_RequiredArgumentAbsent_NamesIt()
    {
        var schema = Schema("""{"type":"object","properties":{"fileUrlOrKey":{"type":"string"},"nodeId":{"type":"string"}},"required":["fileUrlOrKey","nodeId"]}""");

        var missing = ToolArguments.Missing(schema, Arguments("""{"nodeId":"1:2"}"""));

        Assert.Equal(["fileUrlOrKey"], missing);
    }

    [Fact]
    public void Missing_NoArgumentsAtAll_NamesEveryRequiredOne()
    {
        var schema = Schema("""{"type":"object","properties":{"fileUrlOrKey":{"type":"string"},"nodeId":{"type":"string"}},"required":["fileUrlOrKey","nodeId"]}""");

        var missing = ToolArguments.Missing(schema, null);

        Assert.Equal(["fileUrlOrKey", "nodeId"], missing);
    }

    [Theory]
    [InlineData("""{"type":"object","properties":{"depth":{"type":"integer"}}}""")]
    [InlineData("""{"type":"object","properties":{"depth":{"type":"integer"}},"required":[]}""")]
    public void Missing_SchemaRequiresNothing_IsEmpty(string schema)
    {
        var missing = ToolArguments.Missing(Schema(schema), null);

        Assert.Empty(missing);
    }

    [Fact]
    public void Missing_RequiredArgumentPresentAsNull_IsNotMissing()
    {
        var schema = Schema("""{"type":"object","properties":{"fileUrlOrKey":{"type":"string"}},"required":["fileUrlOrKey"]}""");

        var missing = ToolArguments.Missing(schema, Arguments("""{"fileUrlOrKey":null}"""));

        Assert.Empty(missing);
    }
}
