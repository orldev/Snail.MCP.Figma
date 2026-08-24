using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tests.Application.Documents;

public class ActiveDocumentTrackerTests
{
    [Fact]
    public void RemembersLastDocumentAndRejectsGarbage()
    {
        var tracker = new ActiveDocumentTracker();

        Assert.Throws<InvalidOperationException>(() => tracker.Resolve(null));

        Assert.Equal("abc123", tracker.Resolve("https://www.figma.com/design/abc123/My-File"));
        Assert.Equal("abc123", tracker.Resolve(null));

        Assert.Throws<ArgumentException>(() => tracker.Resolve("not a link at all"));
    }
}
