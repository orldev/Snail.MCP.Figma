using System.Globalization;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Application.Documents;

/// <summary>The whole READ channel: file version → cache → (miss) → REST → sanitizer → cache; never touches the plugin.</summary>
public sealed class DocumentReader(
    IPublishedDocumentSource api,
    INodeSnapshotStore cache,
    IDocumentVersionLedger versions,
    NodeSanitizer sanitizer,
    CacheOptions cacheOptions,
    ILogger<DocumentReader> logger)
{
    /// <summary>How long a cached snapshot counts as fresh and is served without asking Figma.</summary>
    private readonly TimeSpan _freshness = TimeSpan.FromSeconds(Math.Max(0, cacheOptions.FreshnessSeconds));

    /// <summary>Reads through the cache; <paramref name="fetch"/> runs only on a miss, costing exactly one Figma request.</summary>
    public async Task<ReadResult> ReadAsync(
        string fileKey,
        string nodeId,
        SanitizationRequest request,
        Func<CancellationToken, Task<FetchedSlice>> fetch,
        CancellationToken cancellationToken = default)
    {
        var variant = BuildVariant(request);

        NodeSnapshot? cached = null;

        if (cacheOptions.Enabled)
        {
            cached = await cache.TryGetAsync(fileKey, nodeId, variant, null, cancellationToken).ConfigureAwait(false);

            if (cached is not null && DateTimeOffset.UtcNow - cached.LastFetchedAt < _freshness)
            {
                logger.LogInformation(
                    "cache hit {FileKey}/{NodeId} variant={Variant} ({Bytes} bytes)",
                    fileKey, nodeId, variant, cached.BytesOut);

                return new ReadResult(
                    JsonNode.Parse(cached.OptimizedJson) ?? new JsonObject(),
                    FromCache: true,
                    cached.BytesIn,
                    cached.BytesOut,
                    cached.FileVersion);
            }
        }

        FetchedSlice fetched;

        try
        {
            fetched = await fetch(cancellationToken).ConfigureAwait(false);
        }
        catch (PublishedSourceException ex) when (cached is not null)
        {
            logger.LogWarning(
                "Figma unavailable ({Error}) — serving {FileKey}/{NodeId} from the cache of {Fetched:u}",
                ex.Message, fileKey, nodeId, cached.LastFetchedAt);

            return new ReadResult(
                JsonNode.Parse(cached.OptimizedJson) ?? new JsonObject(),
                FromCache: true,
                cached.BytesIn,
                cached.BytesOut,
                cached.FileVersion,
                IsStale: true);
        }

        var sanitized = sanitizer.Sanitize(fetched.Node, request, $"{fileKey}/{nodeId}");

        switch (cacheOptions.Enabled)
        {
            case true:
            {
                if (fetched.Version is not null)
                {
                    await versions.RecordAsync(fileKey, fetched.Version, cancellationToken).ConfigureAwait(false);
                }

                await cache.SetAsync(
                    new NodeSnapshot
                    {
                        FileKey = fileKey,
                        NodeId = nodeId,
                        Variant = variant,
                        FileVersion = fetched.Version?.Version,
                        OptimizedJson = sanitized.Node.ToJsonString(),
                        BytesIn = sanitized.BytesIn,
                        BytesOut = sanitized.BytesOut,
                    },
                    cancellationToken).ConfigureAwait(false);
                break;
            }
        }

        return new ReadResult(
            sanitized.Node, FromCache: false, sanitized.BytesIn, sanitized.BytesOut, fetched.Version?.Version);
    }

    /// <summary>The whole file document, or its upper levels.</summary>
    public Task<ReadResult> ReadFileAsync(
        string fileKey,
        DocumentReadOptions options,
        SanitizationRequest request,
        CancellationToken cancellationToken = default) =>
        ReadAsync(
            fileKey,
            NodeSnapshot.WholeFileNodeId,
            request,
            async ct =>
            {
                var raw = await api.GetDocumentAsync(fileKey, options, ct).ConfigureAwait(false);

                return new FetchedSlice(raw, ExtractVersion(raw));
            },
            cancellationToken);

    /// <summary>One node by id.</summary>
    public Task<ReadResult> ReadNodeAsync(
        string fileKey,
        string nodeId,
        NodeReadOptions options,
        SanitizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = NodeId.Normalize(nodeId);

        return ReadAsync(
            fileKey,
            normalized,
            request,
            async ct =>
            {
                var response = await api.GetNodesAsync(fileKey, [normalized], options, ct).ConfigureAwait(false);
                var document = response["nodes"]?[normalized]?["document"]
                               ?? response["nodes"]?[normalized]
                               ?? throw new PublishedSourceException(
                                   SourceFailure.NotFound,
                                   $"Node {normalized} was not found in file {fileKey}",
                                   isAuthError: false);

                return new FetchedSlice(document, ExtractVersion(response));
            },
            cancellationToken);
    }

    /// <summary>Version and modification time from the Figma envelope; <c>null</c> when absent.</summary>
    internal static DocumentVersion? ExtractVersion(JsonNode envelope)
    {
        if (envelope is not JsonObject root)
        {
            return null;
        }

        var version = root["version"]?.GetValue<string>();
        var name = root["name"]?.GetValue<string>();
        var lastModifiedRaw = root["lastModified"]?.GetValue<string>();

        if (version is null && lastModifiedRaw is null)
        {
            return null;
        }

        DateTimeOffset? lastModified = DateTimeOffset.TryParse(
            lastModifiedRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

        return new DocumentVersion(version, lastModified, name);
    }

    /// <summary>Snapshot variant: profile and limits shape the content, hence the cache key.</summary>
    internal static string BuildVariant(SanitizationRequest request) =>
        $"{request.Profile}-d{request.MaxDepth?.ToString() ?? "all"}-n{request.MaxNodes?.ToString() ?? "all"}{(request.DeduplicateStyles ? "-dedup" : string.Empty)}";
}

/// <summary>Figma response together with the file version taken from the same envelope.</summary>
public readonly record struct FetchedSlice(JsonNode Node, DocumentVersion? Version);

/// <summary>Read result with the cache-hit flag and metrics.</summary>
public sealed record ReadResult(
    JsonNode Node,
    bool FromCache,
    long BytesIn,
    long BytesOut,
    string? FileVersion,
    bool IsStale = false)
{
    public double Ratio => BytesIn <= 0 ? 0 : 1d - ((double)BytesOut / BytesIn);
}
