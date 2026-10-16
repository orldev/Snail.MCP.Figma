namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Node rendering by the published source; the most expensive quota bucket.</summary>
public interface IRenderSource
{
    /// <summary>Render links of the nodes in the <c>images</c> map, keyed by node id; dash-form ids get an alias in the response.</summary>
    Task<JsonNode> RenderAsync(string documentKey, IReadOnlyList<string> nodeIds, RenderOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Render options.</summary>
public sealed record RenderOptions
{
    /// <summary>Image scale factor; <c>null</c> leaves the source's default.</summary>
    public double? Scale { get; init; }
    /// <summary>Image format, such as <c>png</c>, <c>jpg</c>, <c>svg</c> or <c>pdf</c>; <c>null</c> leaves the source's default.</summary>
    public string? Format { get; init; }
    /// <summary>Whether content overlapping the node is left out of the render; <c>null</c> leaves the source's default.</summary>
    public bool? ContentsOnly { get; init; }
    /// <summary>Whether SVG text is rendered as outlines rather than text elements; <c>null</c> leaves the source's default.</summary>
    public bool? SvgOutlineText { get; init; }
    /// <summary>Whether SVG elements carry the node ids as <c>id</c> attributes; <c>null</c> leaves the source's default.</summary>
    public bool? SvgIncludeId { get; init; }
    /// <summary>Whether SVG strokes are simplified to plain stroke attributes where possible; <c>null</c> leaves the source's default.</summary>
    public bool? SvgSimplifyStroke { get; init; }
}
