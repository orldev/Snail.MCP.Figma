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

    /// <summary>Local variables and variable collections defined in the document.</summary>
    Task<JsonNode> GetLocalVariablesAsync(string documentKey, CancellationToken cancellationToken = default);

    /// <summary>Variables and variable collections the document publishes to its library.</summary>
    Task<JsonNode> GetPublishedVariablesAsync(string documentKey, CancellationToken cancellationToken = default);

    /// <summary>Styles the document publishes to its library.</summary>
    Task<JsonNode> GetStylesAsync(string documentKey, CancellationToken cancellationToken = default);

    /// <summary>Components the document publishes to its library.</summary>
    Task<JsonNode> GetComponentsAsync(string documentKey, CancellationToken cancellationToken = default);

    /// <summary>Component sets the document publishes to its library.</summary>
    Task<JsonNode> GetComponentSetsAsync(string documentKey, CancellationToken cancellationToken = default);
}

/// <summary>Document read options.</summary>
public sealed record DocumentReadOptions
{
    /// <summary>Historical version; <c>null</c> means current.</summary>
    public string? Version { get; init; }

    /// <summary>Nodes to limit the document to; <c>null</c> or empty reads the whole document.</summary>
    public IReadOnlyList<string>? Ids { get; init; }
    /// <summary>How many levels of the tree to read; <c>null</c> reads the full depth.</summary>
    public int? Depth { get; init; }

    /// <summary>Vector geometry in the response (<c>paths</c>); expensive and rarely needed.</summary>
    public string? Geometry { get; init; }

    /// <summary>Whether the response carries the document's branch metadata.</summary>
    public bool IncludesBranchData { get; init; }
}

/// <summary>Node read options.</summary>
public sealed record NodeReadOptions
{
    /// <summary>Historical version; <c>null</c> means current.</summary>
    public string? Version { get; init; }
    /// <summary>How many levels below each node to read; <c>null</c> reads the full depth.</summary>
    public int? Depth { get; init; }
    /// <summary>Vector geometry in the response (<c>paths</c>); expensive and rarely needed.</summary>
    public string? Geometry { get; init; }
}

/// <summary>Document version — the basis of snapshot validation.</summary>
public sealed record DocumentVersion(string? Version, DateTimeOffset? LastModified, string? Name);
