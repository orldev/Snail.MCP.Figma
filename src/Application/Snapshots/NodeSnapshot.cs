namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Sanitized node snapshot; the same node under another profile or depth is a different snapshot.</summary>
public sealed class NodeSnapshot
{
    /// <summary>Key of the file the node belongs to.</summary>
    public required string FileKey { get; set; }

    /// <summary>Node id in canonical colon form; <c>"__file"</c> means the whole document.</summary>
    public required string NodeId { get; set; }

    /// <summary>Sanitization profile and depth of this snapshot.</summary>
    public required string Variant { get; set; }

    /// <summary>File version at capture time — the cache is invalidated by it.</summary>
    public string? FileVersion { get; set; }

    /// <summary>When the snapshot was stored.</summary>
    public DateTimeOffset LastFetchedAt { get; set; }

    /// <summary>Safety TTL; the cache has no expiry of its own, so the lifetime is tracked here.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Sanitized JSON ready to serve.</summary>
    public required string OptimizedJson { get; set; }

    /// <summary>Size of the source response before sanitization.</summary>
    public long BytesIn { get; set; }

    /// <summary>Size of the sanitized JSON.</summary>
    public long BytesOut { get; set; }

    /// <summary>Node id for a whole-file snapshot.</summary>
    public const string WholeFileNodeId = "__file";
}
