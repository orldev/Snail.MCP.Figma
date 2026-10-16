namespace Snail.MCP.Figma.Application.Diff;

/// <summary>A file or node as it was at a version, and whether the cache answered instead of Figma.</summary>
public readonly record struct VersionSnapshot(JsonNode? Node, bool IsCached);

/// <summary>Files and nodes at past versions through REST: a past version never changes, so it is cached for good; the current one never is.</summary>
public sealed class VersionSnapshots(IPublishedDocumentSource api, VersionSnapshotCache snapshots)
{
    /// <summary>The version id that names the current state rather than a saved version.</summary>
    public const string Current = "current";

    /// <summary>Whether the id names the current state.</summary>
    public static bool IsCurrent(string versionId) =>
        string.Equals(versionId, Current, StringComparison.OrdinalIgnoreCase);

    /// <summary>File snapshot at a version, depth 1.</summary>
    public async Task<VersionSnapshot> FileAsync(string fileKey, string versionId, CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, 1, null);

        if (snapshots.Get(key) is { } cached)
        {
            return new VersionSnapshot(cached, IsCached: true);
        }

        var file = await api.GetDocumentAsync(
            fileKey,
            new DocumentReadOptions { Depth = 1, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, file);

        return new VersionSnapshot(file, IsCached: false);
    }

    /// <summary>Node snapshot at a version, depth 2.</summary>
    public async Task<VersionSnapshot> NodeAsync(string fileKey, string nodeId, string versionId, CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, 2, [nodeId]);

        if (snapshots.Get(key) is { } cached)
        {
            return new VersionSnapshot(cached["nodes"]?[nodeId]?["document"], IsCached: true);
        }

        var response = await api.GetNodesAsync(
            fileKey,
            [nodeId],
            new NodeReadOptions { Depth = 2, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, response);

        return new VersionSnapshot(response["nodes"]?[nodeId]?["document"], IsCached: false);
    }

    /// <summary>The whole file at a version, to the depth asked; <c>null</c> depth reads all of it.</summary>
    public async Task<JsonNode?> FileAtAsync(string fileKey, string versionId, int? depth, CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, depth, null);

        if (snapshots.Get(key) is { } cached)
        {
            return cached;
        }

        var file = await api.GetDocumentAsync(
            fileKey,
            new DocumentReadOptions { Depth = depth, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, file);

        return file;
    }

    /// <summary>Nodes at a version, to the depth asked.</summary>
    public async Task<JsonNode?> NodesAtAsync(
        string fileKey,
        IReadOnlyList<string> nodeIds,
        string versionId,
        int depth,
        CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, depth, nodeIds);

        if (snapshots.Get(key) is { } cached)
        {
            return cached["nodes"];
        }

        var response = await api.GetNodesAsync(
            fileKey,
            nodeIds,
            new NodeReadOptions { Depth = depth, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, response);

        return response["nodes"];
    }
}
