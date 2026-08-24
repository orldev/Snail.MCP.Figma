namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Published components of team libraries by key, without access to the document itself.</summary>
public interface ILibraryCatalog
{
    /// <summary>Throws <see cref="PublishedSourceException"/> with <see cref="SourceFailure.NotFound"/> when the key is not a component.</summary>
    Task<JsonNode> GetPublishedComponentAsync(string componentKey, CancellationToken cancellationToken = default);

    Task<JsonNode> GetPublishedComponentSetAsync(string componentSetKey, CancellationToken cancellationToken = default);

    /// <summary>Published style by key; throws <see cref="SourceFailure.NotFound"/> when the key is not a style.</summary>
    Task<JsonNode> GetPublishedStyleAsync(string styleKey, CancellationToken cancellationToken = default);

    /// <summary>One page of the published components of a team library.</summary>
    Task<JsonNode> GetTeamComponentsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default);

    /// <summary>One page of the published component sets of a team library.</summary>
    Task<JsonNode> GetTeamComponentSetsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default);
}
