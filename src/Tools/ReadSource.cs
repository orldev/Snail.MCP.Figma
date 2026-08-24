namespace Snail.MCP.Figma.Tools;

/// <summary>Data source of the READ channel; the source of truth for the <c>source</c> parameter.</summary>
public enum ReadSource
{
    /// <summary>Plugin when a live session exists for the file, otherwise REST.</summary>
    Auto,

    /// <summary>REST only: last saved state, costs quota.</summary>
    Rest,

    /// <summary>Plugin only, no fallback.</summary>
    Plugin,
}
