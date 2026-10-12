namespace Snail.MCP.Figma.Application.Snapshots;

/// <summary>Snapshot invalidation driven by plugin events: the only honest signal that data went stale.</summary>
public sealed class SnapshotInvalidator(
    INodeSnapshotStore cache,
    IRenderSnapshotStore images,
    IDocumentVersionLedger versions,
    ILogger<SnapshotInvalidator> logger)
{
    /// <summary>Document changed: drops the given nodes or the whole file, and forgets the version so the next read asks Figma.</summary>
    public async Task OnDocumentChangedAsync(
        string? fileKey,
        IReadOnlyCollection<string> changedNodeIds,
        bool hasStyleChanges,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return;
        }

        await versions.ForgetAsync(fileKey, cancellationToken).ConfigureAwait(false);

        await images.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);

        if (hasStyleChanges || changedNodeIds.Count == 0)
        {
            await cache.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);

            return;
        }

        var removed = await cache.InvalidateNodesAsync(fileKey, changedNodeIds, cancellationToken).ConfigureAwait(false);
        Log.DocumentChanged(logger, fileKey, changedNodeIds.Count, removed);
    }

    /// <summary>After a plugin write: invalidates the nodes and their ancestors, since Auto Layout reflows upwards.</summary>
    public async Task OnNodesMutatedAsync(
        string? fileKey,
        IReadOnlyCollection<string> nodeIds,
        IReadOnlyCollection<string> ancestorIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileKey) || (nodeIds.Count == 0 && ancestorIds.Count == 0))
        {
            return;
        }

        await versions.ForgetAsync(fileKey, cancellationToken).ConfigureAwait(false);

        var all = nodeIds.Concat(ancestorIds).Distinct(StringComparer.Ordinal).ToArray();
        await cache.InvalidateNodesAsync(fileKey, all, cancellationToken).ConfigureAwait(false);

        await images.InvalidateFileAsync(fileKey, cancellationToken).ConfigureAwait(false);
    }
}
