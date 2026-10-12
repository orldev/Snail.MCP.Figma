using System.Globalization;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Application.Documents;

/// <summary>Renders nodes through the cache; only misses reach Figma, in a single request.</summary>
public sealed class DocumentRenderer(
    IRenderSource api,
    IRenderSnapshotStore cache,
    IDocumentVersionLedger versions,
    ILogger<DocumentRenderer> logger)
{
    /// <summary>Render variant key: format and scale produce different pictures of one node.</summary>
    public static string BuildVariant(string format, double scale) =>
        $"{format.ToLowerInvariant()}@{scale.ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>Link strictly from the cache, without touching Figma.</summary>
    public async Task<string?> TryGetCachedAsync(
        string fileKey,
        string nodeId,
        string format,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var cached = await cache.TryGetAsync(
            fileKey,
            NodeId.Normalize(nodeId),
            BuildVariant(format, scale),
            KnownVersion(fileKey),
            cancellationToken).ConfigureAwait(false);

        return cached?.Url;
    }

    /// <summary>Renders nodes: cached links are reused, misses are fetched in one request and cached.</summary>
    public async Task<ImageRenderResult> RenderAsync(
        string fileKey,
        IReadOnlyList<string> nodeIds,
        string format,
        double scale,
        CancellationToken cancellationToken = default)
    {
        var variant = BuildVariant(format, scale);
        var version = KnownVersion(fileKey);

        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        var fromCache = new List<string>();
        var misses = new List<string>();

        foreach (var raw in nodeIds)
        {
            var nodeId = NodeId.Normalize(raw);
            var cached = await cache.TryGetAsync(fileKey, nodeId, variant, version, cancellationToken).ConfigureAwait(false);

            if (cached is not null)
            {
                urls[nodeId] = cached.Url;
                fromCache.Add(nodeId);
            }
            else
            {
                misses.Add(nodeId);
            }
        }

        if (misses.Count == 0)
        {
            logger.LogDebug("Render {FileKey}: all {Count} nodes from cache, Figma not called", fileKey, fromCache.Count);

            return new ImageRenderResult(urls, fromCache, []);
        }

        var response = await api
            .RenderAsync(fileKey, misses, new RenderOptions { Format = format.ToLowerInvariant(), Scale = scale }, cancellationToken)
            .ConfigureAwait(false);

        var fetched = new List<string>();

        switch (response["images"])
        {
            case JsonObject images:
            {
                foreach (var nodeId in misses)
                {
                    if (images[nodeId]?.GetValue<string>() is not { Length: > 0 } url)
                    {
                        continue;
                    }

                    urls[nodeId] = url;
                    fetched.Add(nodeId);

                    await cache.SetAsync(
                        new RenderSnapshot
                        {
                            FileKey = fileKey,
                            NodeId = nodeId,
                            Variant = variant,
                            FileVersion = version,
                            Url = url,
                        },
                        cancellationToken).ConfigureAwait(false);
                }

                break;
            }
        }

        return new ImageRenderResult(urls, fromCache, fetched);
    }

    /// <summary>Last known file version, taken from the ledger and never requested separately.</summary>
    private string? KnownVersion(string fileKey) => versions.GetStored(fileKey)?.Version;
}

/// <summary>Render result: links per node plus where each came from.</summary>
public sealed record ImageRenderResult(
    IReadOnlyDictionary<string, string> Urls,
    IReadOnlyCollection<string> FromCache,
    IReadOnlyCollection<string> Fetched)
{
    /// <summary>No node required a Figma request.</summary>
    public bool FullyFromCache => Fetched.Count == 0;
}
