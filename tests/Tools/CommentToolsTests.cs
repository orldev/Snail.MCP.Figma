using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Tools;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Tests.Tools;

/// <summary>Comment tool behaviour over the published source: resolved threads, pinning, escaping.</summary>
[Collection(nameof(BridgeHostFixture))]
public class CommentToolsTests
{
    private const string FileUrl = "https://www.figma.com/design/ABC123XYZ/Design-System";

    private readonly BridgeHostFixture _bridge;

    public CommentToolsTests(BridgeHostFixture bridge) => _bridge = bridge;

    private CommentTools Comments(FakePublishedSource api) => new(
        _bridge.Gateway, api, new ActiveDocumentTracker(), NullLogger<CommentTools>.Instance);

    private static JsonNode Data(string response)
    {
        var envelope = Parse(response);
        Assert.True(envelope["ok"]!.GetValue<bool>(), envelope["error"]?.GetValue<string>() ?? "expected success");

        return envelope["data"]!;
    }

    private static JsonNode Parse(string response) => JsonNode.Parse(response)!;

    private static FakePublishedSource CommentApi()
    {
        var api = new FakePublishedSource(new JsonObject());

        api.Comments.Add(new JsonObject { ["id"] = "1", ["message"] = "Open", ["resolved_at"] = null });
        api.Comments.Add(new JsonObject { ["id"] = "2", ["message"] = "Resolved", ["resolved_at"] = "2026-01-01T00:00:00Z" });

        return api;
    }

    [Fact]
    public async Task ResolvedThreadsAreHiddenByDefaultButCounted()
    {
        var api = CommentApi();

        var data = Data(await Comments(api).GetCommentsAsync(FileUrl));

        Assert.Single(data["comments"]!.AsArray());
        Assert.Equal("1", data["comments"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(2, data["summary"]!["total"]!.GetValue<int>());
        Assert.Equal(1, data["summary"]!["returned"]!.GetValue<int>());
    }

    [Fact]
    public async Task ResolvedThreadsComeBackWhenAskedFor()
    {
        var api = CommentApi();

        var data = Data(await Comments(api).GetCommentsAsync(FileUrl, asMarkdown: true, includeResolved: true));

        Assert.Equal(2, data["comments"]!.AsArray().Count);
        Assert.Contains("as_md=true", api.RequestedEndpoints[0]);
    }

    [Fact]
    public async Task CommentPinnedToANodeCarriesClientMeta()
    {
        var api = CommentApi();

        var data = Data(await Comments(api).PostCommentAsync("Wrong spacing", FileUrl, "10-2", x: 4, y: 8));
        var body = data["body"]!;

        Assert.Equal("/v1/files/ABC123XYZ/comments", data["endpoint"]!.GetValue<string>());
        Assert.Equal("Wrong spacing", body["message"]!.GetValue<string>());
        Assert.Equal("10:2", body["client_meta"]!["node_id"]!.GetValue<string>());
        Assert.Equal(4, body["client_meta"]!["node_offset"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public async Task ReplyWinsOverPinning()
    {
        var api = CommentApi();

        var data = Data(await Comments(api).PostCommentAsync("Fixed", FileUrl, "10:2", replyToCommentId: "77"));
        var body = data["body"]!;

        Assert.Equal("77", body["comment_id"]!.GetValue<string>());
        Assert.Null(body["client_meta"]);
    }

    [Fact]
    public async Task CommentIdIsEscapedIntoTheDeleteUrl()
    {
        var api = CommentApi();

        var data = Data(await Comments(api).DeleteCommentAsync("id/with slash", FileUrl));

        Assert.True(data["deleted"]!.GetValue<bool>());
        Assert.Equal("/v1/files/ABC123XYZ/comments/id%2Fwith%20slash",
            api.LastDeletedEndpoint);
    }
}
