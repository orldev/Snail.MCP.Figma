using ModelContextProtocol.Protocol;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Tools;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>The string answers of the tools are Snail.Sdk's envelope, with the fields the tools' contract keeps beside <c>data</c>, so the pipeline flags their failures and a client reads them as every server's.</summary>
public class AnswerTextTests
{
    private static CallToolResult Answer(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    /// <summary>The fields a read carries beside its data — file, source, savings — stay at the top level, after ok and data.</summary>
    [Fact]
    public void OkText_FieldsBeside_StayAfterTheData()
    {
        var text = ToolAnswer.OkText(new JsonObject { ["name"] = "Кнопка" }, new JsonObject { ["fileKey"] = "abc", ["note"] = null });

        Assert.Equal("""{"ok":true,"data":{"name":"Кнопка"},"fileKey":"abc"}""", text);
        Assert.False(ToolAnswer.IsFailure(Answer(text)));
    }

    [Fact]
    public void FailedText_WithBreakdown_IsTheFailureEnvelope()
    {
        var text = ToolAnswer.FailedText("2 of 3 failed.", data: new JsonArray(new JsonObject { ["ok"] = true }));

        Assert.Equal("""{"ok":false,"error":"2 of 3 failed.","data":[{"ok":true}]}""", text);
        Assert.True(ToolAnswer.IsFailure(Answer(text)));
    }

    /// <summary>A success that says what to do next — an empty page, more matches, what a saved version's id is for — carries its hint before the data.</summary>
    [Fact]
    public void OkText_WithAHint_WritesItBeforeTheData()
    {
        var text = ToolAnswer.OkText(new JsonObject { ["items"] = new JsonArray() }, hint: "Widen the query.");

        Assert.Equal("""{"ok":true,"hint":"Widen the query.","data":{"items":[]}}""", text);
        Assert.False(ToolAnswer.IsFailure(Answer(text)));
        Assert.Equal("Widen the query.", Answer(text).Hint());
    }

    [Fact]
    public void OkText_WithoutAHint_IsThePlainSuccess()
    {
        var text = ToolAnswer.OkText(new JsonObject { ["ok"] = false }, hint: null);

        Assert.Equal("""{"ok":true,"data":{"ok":false}}""", text);
    }

    [Fact]
    public void AnswerFields_ReadFromFigma_NamesTheSourceAndTheSavings()
    {
        var fields = AnswerFields.Of(new ReadResult(new JsonObject(), IsFromCache: false, BytesIn: 1000, BytesOut: 250, FileVersion: "42"), "abc");

        Assert.Equal("""{"fileKey":"abc","source":"figma-api","fileVersion":"42","metrics":{"bytesIn":1000,"bytesOut":250,"savedRatio":0.75}}""", fields.ToJsonString());
    }

    [Fact]
    public void AnswerFields_StaleSnapshot_SaysWhy()
    {
        var fields = AnswerFields.Of(new ReadResult(new JsonObject(), IsFromCache: true, BytesIn: 0, BytesOut: 0, FileVersion: null, IsStale: true), "abc");

        Assert.Equal("cache-stale", fields["source"]!.GetValue<string>());
        Assert.Contains("may be stale", fields["staleReason"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
