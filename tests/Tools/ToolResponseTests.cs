using ModelContextProtocol.Protocol;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Which answers are the failure envelope, the reading the pipeline flags isError by.</summary>
public class ToolResponseTests
{
    private static CallToolResult Answer(params string[] texts) =>
        new() { Content = [.. texts.Select(text => (ContentBlock)new TextContentBlock { Text = text })] };

    [Fact]
    public void IsFailure_ErrorEnvelope_IsTrue()
    {
        var answer = ToolResponse.Textual(ToolResponse.Error("The node was not found.", "Read the page first."));

        Assert.True(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_ErrorEnvelopeWithPartialData_IsTrue()
    {
        var answer = ToolResponse.Textual(ToolResponse.Error("2 of 3 failed.", data: new JsonArray(new JsonObject { ["ok"] = true })));

        Assert.True(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_SuccessEnvelope_IsFalse()
    {
        var answer = ToolResponse.Textual(ToolResponse.Ok(new JsonObject { ["ok"] = false }));

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_StructuredSuccess_IsFalse()
    {
        var answer = ToolResponse.Structured(new JsonObject { ["connected"] = false });

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_OkAfterOtherProperties_IsTrue()
    {
        var answer = Answer("""{"data":{"ok":true,"items":[1,2]},"ok":false,"error":"late"}""");

        Assert.True(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_OkOnlyNestedDeeper_IsFalse()
    {
        var answer = Answer("""{"report":{"ok":false}}""");

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_TextThatIsNotJson_IsFalse()
    {
        var answer = Answer("{ok:false — not JSON");

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_PlainText_IsFalse()
    {
        var answer = Answer("ok: false");

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_SeveralBlocks_IsFalse()
    {
        var answer = Answer(ToolResponse.Error("first"), ToolResponse.Error("second"));

        Assert.False(ToolResponse.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_NoContent_IsFalse()
    {
        var answer = Answer();

        Assert.False(ToolResponse.IsFailure(answer));
    }
}
