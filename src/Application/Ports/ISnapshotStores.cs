using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Store of sanitized node snapshots; failures are swallowed because snapshots are an optimization.</summary>
public interface INodeSnapshotStore
{
    /// <summary>Fresh snapshot or <c>null</c>; a snapshot of another document version is not fresh.</summary>
    Task<NodeSnapshot?> TryGetAsync(string fileKey, string nodeId, string variant, string? currentFileVersion, CancellationToken cancellationToken = default);

    /// <summary>Stores the snapshot, replacing any held for the same node and variant.</summary>
    Task SetAsync(NodeSnapshot node, CancellationToken cancellationToken = default);

    /// <summary>Targeted invalidation; returns the number of removed snapshots.</summary>
    Task<int> InvalidateNodesAsync(string fileKey, IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default);

    /// <summary>Drops every snapshot of the document; returns the number of removed snapshots.</summary>
    Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default);
}

/// <summary>Store of render links; usually invalidated per file because the cached render is typically the parent's.</summary>
public interface IRenderSnapshotStore
{
    /// <summary>Unexpired render link or <c>null</c>; a link taken on another document version is not fresh.</summary>
    Task<RenderSnapshot?> TryGetAsync(string fileKey, string nodeId, string variant, string? currentFileVersion, CancellationToken cancellationToken = default);

    /// <summary>Stores the render link, replacing any held for the same node and variant.</summary>
    Task SetAsync(RenderSnapshot image, CancellationToken cancellationToken = default);

    /// <summary>Targeted invalidation; returns the number of removed render links.</summary>
    Task<int> InvalidateNodesAsync(string fileKey, IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default);

    /// <summary>Drops every render link of the document; returns the number removed.</summary>
    Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default);
}

/// <summary>Ledger of document versions that decides whether a snapshot is alive.</summary>
public interface IDocumentVersionLedger
{
    /// <summary>Records the version from the response envelope; a change drops the document's snapshots.</summary>
    Task RecordAsync(string fileKey, DocumentVersion info, CancellationToken cancellationToken = default);

    /// <summary>Last recorded version of the document; <c>null</c> when none is recorded or the ledger cannot be read.</summary>
    DocumentVersionRecord? GetStored(string fileKey);

    /// <summary>Forgets the recorded version, so the next read records it afresh.</summary>
    Task ForgetAsync(string fileKey, CancellationToken cancellationToken = default);
}
