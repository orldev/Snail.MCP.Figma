namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Published source: the last SAVED state of a document; reads everything at quota cost, never writes.</summary>
public interface IPublishedDocumentSource
{
    /// <summary>The source is configured and can be called.</summary>
    bool IsAvailable { get; }

    /// <summary>Name, last-modified and editor type without downloading the document — the cheap change check.</summary>
    Task<JsonNode> GetFileMetaAsync(string documentKey, CancellationToken cancellationToken = default);

    /// <summary>Whole document or a slice of it.</summary>
    Task<JsonNode> GetDocumentAsync(string documentKey, DocumentReadOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Nodes by id; dash-form ids get an alias in the response.</summary>
    Task<JsonNode> GetNodesAsync(string documentKey, IReadOnlyList<string> nodeIds, NodeReadOptions? options = null, CancellationToken cancellationToken = default);

    Task<JsonNode> GetLocalVariablesAsync(string documentKey, CancellationToken cancellationToken = default);

    Task<JsonNode> GetPublishedVariablesAsync(string documentKey, CancellationToken cancellationToken = default);

    Task<JsonNode> GetStylesAsync(string documentKey, CancellationToken cancellationToken = default);

    Task<JsonNode> GetComponentsAsync(string documentKey, CancellationToken cancellationToken = default);

    Task<JsonNode> GetComponentSetsAsync(string documentKey, CancellationToken cancellationToken = default);
}

/// <summary>Document read options.</summary>
public sealed record DocumentReadOptions
{
    /// <summary>Historical version; <c>null</c> means current.</summary>
    public string? Version { get; init; }

    public IReadOnlyList<string>? Ids { get; init; }
    public int? Depth { get; init; }

    /// <summary>Vector geometry in the response (<c>paths</c>); expensive and rarely needed.</summary>
    public string? Geometry { get; init; }

    public bool IncludesBranchData { get; init; }
}

/// <summary>Node read options.</summary>
public sealed record NodeReadOptions
{
    public string? Version { get; init; }
    public int? Depth { get; init; }
    public string? Geometry { get; init; }
}

/// <summary>Document version — the basis of snapshot validation.</summary>
public sealed record DocumentVersion(string? Version, DateTimeOffset? LastModified, string? Name);
