using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Store of sanitized node snapshots; failures are swallowed because snapshots are an optimization.</summary>
public interface INodeSnapshotStore
{
    /// <summary>Fresh snapshot or <c>null</c>; a snapshot of another document version is not fresh.</summary>
    Task<NodeSnapshot?> TryGetAsync(string fileKey, string nodeId, string variant, string? currentFileVersion, CancellationToken cancellationToken = default);

    Task SetAsync(NodeSnapshot node, CancellationToken cancellationToken = default);

    /// <summary>Targeted invalidation; returns the number of removed snapshots.</summary>
    Task<int> InvalidateNodesAsync(string fileKey, IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default);

    Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default);
}

/// <summary>Store of render links; usually invalidated per file because the cached render is typically the parent's.</summary>
public interface IRenderSnapshotStore
{
    Task<RenderSnapshot?> TryGetAsync(string fileKey, string nodeId, string variant, string? currentFileVersion, CancellationToken cancellationToken = default);

    Task SetAsync(RenderSnapshot image, CancellationToken cancellationToken = default);

    Task<int> InvalidateNodesAsync(string fileKey, IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken = default);

    Task<int> InvalidateFileAsync(string fileKey, CancellationToken cancellationToken = default);
}

/// <summary>Ledger of document versions that decides whether a snapshot is alive.</summary>
public interface IDocumentVersionLedger
{
    /// <summary>Records the version from the response envelope; a change drops the document's snapshots.</summary>
    Task RecordAsync(string fileKey, DocumentVersion info, CancellationToken cancellationToken = default);

    DocumentVersionRecord? GetStored(string fileKey);

    Task ForgetAsync(string fileKey, CancellationToken cancellationToken = default);
}
