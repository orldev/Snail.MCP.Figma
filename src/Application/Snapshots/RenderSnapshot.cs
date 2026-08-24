namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Link to a rendered node picture; rendering is the most expensive bucket while the link lives 30 days.</summary>
public sealed class RenderSnapshot
{
    public string? Id { get; set; }

    public required string FileKey { get; set; }

    /// <summary>Node id in canonical colon form.</summary>
    public required string NodeId { get; set; }

    /// <summary>Render format and scale, e.g. <c>png@2</c>.</summary>
    public required string Variant { get; set; }

    /// <summary>File version at render time, if known; taken from the ledger at no cost, a mismatch is a miss.</summary>
    public string? FileVersion { get; set; }

    /// <summary>Link returned by Figma; valid for 30 days.</summary>
    public required string Url { get; set; }

    public DateTimeOffset LastFetchedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
