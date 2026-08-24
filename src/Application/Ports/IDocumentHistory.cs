namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Document version history, available only through the published source.</summary>
public interface IDocumentHistory
{
    /// <summary>Page of versions from newest to oldest; <paramref name="after"/> is a cursor pointing to older entries.</summary>
    Task<JsonNode> GetVersionsPageAsync(string documentKey, int pageSize, string? after, CancellationToken cancellationToken = default);
}
