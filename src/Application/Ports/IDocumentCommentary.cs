namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Document comments — the only thing the published source can write.</summary>
public interface IDocumentCommentary
{
    /// <summary>Comments of the document, with message bodies as Markdown when <paramref name="asMarkdown"/> is set.</summary>
    Task<JsonNode> GetCommentsAsync(string documentKey, bool asMarkdown, CancellationToken cancellationToken = default);

    /// <summary>Posts a comment; the body is in source shape (<c>message</c>, <c>comment_id</c>, <c>client_meta</c>).</summary>
    Task<JsonNode> PostCommentAsync(string documentKey, JsonNode body, CancellationToken cancellationToken = default);

    /// <summary>Deletes the comment from the document.</summary>
    Task DeleteCommentAsync(string documentKey, string commentId, CancellationToken cancellationToken = default);
}
