namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Sanitized node snapshot; the same node under another profile or depth is a different snapshot.</summary>
public sealed class NodeSnapshot
{
    public string? Id { get; set; }

    public required string FileKey { get; set; }

    /// <summary>Node id in canonical colon form; <c>"__file"</c> means the whole document.</summary>
    public required string NodeId { get; set; }

    /// <summary>Sanitization profile and depth of this snapshot.</summary>
    public required string Variant { get; set; }

    /// <summary>File version at capture time — the cache is invalidated by it.</summary>
    public string? FileVersion { get; set; }

    public DateTimeOffset LastFetchedAt { get; set; }

    /// <summary>Safety TTL; LiteDB has no expiration bundle, so the lifetime is tracked here.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Sanitized JSON ready to serve.</summary>
    public required string OptimizedJson { get; set; }

    public long BytesIn { get; set; }

    public long BytesOut { get; set; }

    /// <summary>Node id for a whole-file snapshot.</summary>
    public const string WholeFileNodeId = "__file";
}
