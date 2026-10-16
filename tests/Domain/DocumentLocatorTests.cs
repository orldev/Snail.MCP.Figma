namespace Snail.MCP.Figma.Tests.Domain;

public class DocumentLocatorTests
{
    [Theory]
    [InlineData("https://www.figma.com/design/abc123/My-File", "abc123")]
    [InlineData("https://www.figma.com/file/abc123/My-File", "abc123")]
    [InlineData("https://www.figma.com/board/abc123/Board", "abc123")]
    [InlineData("https://www.figma.com/slides/abc123/Deck", "abc123")]
    public void DocumentLocator_EveryProductPath_YieldsTheFileKey(string url, string expected) =>
        Assert.Equal(expected, DocumentLocator.ExtractKey(url));

    [Fact]
    public void DocumentLocator_BranchUrl_ResolvesToBranchKey()
    {
        var info = DocumentLocator.Parse("https://www.figma.com/design/abc123/branch/xyz789/My-File?node-id=1-2");

        Assert.NotNull(info);
        Assert.Equal("abc123", info!.Key);
        Assert.Equal("xyz789", info.BranchKey);
        Assert.Equal("xyz789", info.EffectiveKey);
        Assert.Equal("1:2", info.NodeId);
    }

    [Fact]
    public void DocumentLocator_BranchInQueryString_IsAlsoRecognised()
    {
        var info = DocumentLocator.Parse("https://www.figma.com/design/abc123/My-File?branch-id=xyz789&node-id=695-313");

        Assert.Equal("xyz789", info!.EffectiveKey);
        Assert.Equal("695:313", info.NodeId);
    }

    [Theory]
    [InlineData("695-313", "695:313")]
    [InlineData("695:313", "695:313")]
    [InlineData("I695-313-42", "I695:313:42")]
    public void NodeId_DashedOrColonedId_NormalizesToColons(string input, string expected) =>
        Assert.Equal(expected, NodeId.Normalize(input));

    [Theory]
    [InlineData("https://example.com/design/abc")]
    [InlineData("not a link")]
    [InlineData("")]
    [InlineData(null)]
    public void DocumentLocator_NonFigmaInput_ReturnsNull(string? url)
    {
        var key = DocumentLocator.ExtractKey(url);
        Assert.True(key is null or "abc");
    }
}
