namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Node rendering by the published source; the most expensive quota bucket.</summary>
public interface IRenderSource
{
    Task<JsonNode> RenderAsync(string documentKey, IReadOnlyList<string> nodeIds, RenderOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Render options.</summary>
public sealed record RenderOptions
{
    public double? Scale { get; init; }
    public string? Format { get; init; }
    public bool? ContentsOnly { get; init; }
    public bool? SvgOutlineText { get; init; }
    public bool? SvgIncludeId { get; init; }
    public bool? SvgSimplifyStroke { get; init; }
}
