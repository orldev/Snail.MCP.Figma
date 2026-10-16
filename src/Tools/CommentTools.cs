using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>File comments — the only place where REST writes.</summary>
[McpServerToolType]
public sealed class CommentTools(
    ILiveEditor plugin,
    IDocumentCommentary comments,
    ActiveDocumentTracker activeFile,
    ILogger<CommentTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Lists the file's comment threads through REST, leaving resolved ones out unless asked.</summary>
    [McpServerTool(Name = "figma_get_comments", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Comment.GetComments)]
    public Task<string> GetCommentsAsync(
        [Description("Link to the Figma file. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Return comment text as Markdown.")] bool asMarkdown = false,
        [Description("Include resolved threads.")] bool includeResolved = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var response = await comments.GetCommentsAsync(fileKey, asMarkdown, ct).ConfigureAwait(false);
            var all = response["comments"] as JsonArray ?? [];

            static bool IsResolved(JsonNode? comment) =>
                comment?["resolved_at"] is { } resolved && resolved.GetValueKind() != System.Text.Json.JsonValueKind.Null;

            var comments1 = new JsonArray(all
                .Where(comment => includeResolved || !IsResolved(comment))
                .Select(comment => comment!.DeepClone())
                .ToArray());

            return ToolResponse.Ok(new JsonObject
            {
                ["fileKey"] = fileKey,
                ["comments"] = comments1,
                ["summary"] = new JsonObject
                {
                    ["returned"] = comments1.Count,
                    ["total"] = all.Count,
                    ["includeResolved"] = includeResolved,
                },
            });
        }, cancellationToken);

    /// <summary>Posts a new comment or a reply to a thread through REST, optionally pinned to a point on a node.</summary>
    [McpServerTool(Name = "figma_post_comment", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
    [Description(ToolDescriptions.Comment.PostComment)]
    public Task<string> PostCommentAsync(
        [Description("Comment text.")] string message,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Id of the node to pin the comment to.")] string? nodeId = null,
        [Description("X coordinate of the point on the canvas. Works together with nodeId.")] double? x = null,
        [Description("Y coordinate of the point on the canvas. Works together with nodeId.")] double? y = null,
        [Description("Id of the comment being replied to.")] string? replyToCommentId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);

            var body = new JsonObject { ["message"] = message };

            if (!string.IsNullOrWhiteSpace(replyToCommentId))
            {
                body["comment_id"] = replyToCommentId;
            }
            else if (!string.IsNullOrWhiteSpace(nodeId))
            {
                body["client_meta"] = new JsonObject
                {
                    ["node_id"] = NodeId.Normalize(nodeId),
                    ["node_offset"] = new JsonObject { ["x"] = x ?? 0, ["y"] = y ?? 0 },
                };
            }

            var response = await comments.PostCommentAsync(fileKey, body, ct).ConfigureAwait(false);

            return ToolResponse.Ok(response, envelope => envelope["fileKey"] = fileKey);
        }, cancellationToken);

    /// <summary>Removes one comment from the file through REST.</summary>
    [McpServerTool(Name = "figma_delete_comment", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Comment.DeleteComment)]
    public Task<string> DeleteCommentAsync(
        [Description("Comment id.")] string commentId,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);

            await comments.DeleteCommentAsync(fileKey, commentId, ct).ConfigureAwait(false);

            return ToolResponse.Ok(new JsonObject
            {
                ["deleted"] = true,
                ["commentId"] = commentId,
                ["fileKey"] = fileKey,
            });
        }, cancellationToken);
}
