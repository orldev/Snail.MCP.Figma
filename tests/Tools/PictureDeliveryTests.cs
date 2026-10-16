using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>The delivery policy decides what a caller in another process gets, so its refusals and its ceiling are worth pinning down away from the bridge.</summary>
public class PictureDeliveryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("path")]
    public void PictureDelivery_DeliverEmptyOrPath_DefaultsToPathWithoutPicture(string? deliver)
    {
        Assert.True(PictureDelivery.TryParse(deliver, out var inline, out var error));
        Assert.False(inline);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("both")]
    [InlineData("BOTH")]
    [InlineData(" Both ")]
    public void PictureDelivery_BothInAnyCasing_ParsesAsInline(string deliver)
    {
        Assert.True(PictureDelivery.TryParse(deliver, out var inline, out _));
        Assert.True(inline);
    }

    [Fact]
    public void PictureDelivery_UnknownValue_IsRefusedNotDefaulted()
    {
        Assert.False(PictureDelivery.TryParse("content", out _, out var error));
        Assert.Contains("content", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void PictureDelivery_PngUnderCeiling_TravelsWithMimeType()
    {
        var picture = new JsonObject { ["base64"] = "aGVsbG8=", ["format"] = "PNG" };

        var shown = Assert.NotNull(PictureDelivery.From(picture, inline: true));

        Assert.Equal("aGVsbG8=", shown.Base64);
        Assert.Equal("image/png", shown.MimeType);
    }

    [Fact]
    public void PictureDelivery_JpgOrSvg_OnlyJpgIsAPicture()
    {
        var jpg = PictureDelivery.From(new JsonObject { ["base64"] = "aGk=", ["format"] = "JPG" }, inline: true);
        var svg = PictureDelivery.From(new JsonObject { ["base64"] = "aGk=", ["format"] = "SVG" }, inline: true);

        Assert.Equal("image/jpeg", Assert.NotNull(jpg).MimeType);
        Assert.Null(svg);
    }

    [Fact]
    public void PictureDelivery_InlineNotAsked_LeavesPictureOnDisk()
    {
        var picture = new JsonObject { ["base64"] = "aGVsbG8=", ["format"] = "PNG" };

        Assert.Null(PictureDelivery.From(picture, inline: false));
    }

    [Fact]
    public void PictureDelivery_PictureOverCeiling_StaysAPath()
    {
        var oversized = new string('A', (int)(ImageLimits.MaxInlineBytes / 3 * 4) + 8);
        var picture = new JsonObject { ["base64"] = oversized, ["format"] = "PNG" };

        Assert.Null(PictureDelivery.From(picture, inline: true));
    }
}
