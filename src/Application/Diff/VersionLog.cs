namespace Snail.MCP.Figma.Application.Diff;

/// <summary>Versions older than a start, newest first, as far as the walk got; <see cref="Failure"/> says why it stopped early.</summary>
public sealed record CandidateVersions(IReadOnlyList<JsonObject> Versions, int ApiCalls, string? Failure)
{
    /// <summary>Whether the walk broke off before it reached the lookback.</summary>
    public bool IsBroken => Failure is not null;
}

/// <summary>A file's version history through REST: the list a person reads, the range a blame searches, the authors a changelog names.</summary>
public sealed class VersionLog(IDocumentHistory history, ILogger<VersionLog> logger)
{
    private const int FigmaPageSizeMax = 50;

    /// <summary>The most versions one listing returns.</summary>
    public const int MaxVersionsHardCap = 200;

    /// <summary>Page cap when walking history — a guard against endless loops on broken pagination.</summary>
    private const int MaxScanPages = 20;

    /// <summary>The versions of the file, newest first, autosaves left out unless asked for, with a cursor that loses none.</summary>
    public async Task<JsonObject> ListAsync(
        string fileKey,
        bool includeAutosaves,
        int maxVersions,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var cap = Math.Clamp(maxVersions, 1, MaxVersionsHardCap);

        var collected = new JsonArray();
        var filteredOut = 0;
        var apiCalls = 0;
        var pages = 0;
        var pageCursor = cursor;

        string? lastCollectedId = null;
        var moreAvailable = false;

        while (pages < MaxScanPages)
        {
            var response = await PageAsync(fileKey, pageCursor, cancellationToken).ConfigureAwait(false);
            pages++;
            apiCalls++;

            var versions = response["versions"] as JsonArray ?? [];

            if (versions.Count == 0)
            {
                break;
            }

            var lastReceivedId = versions[^1]?["id"]?.GetValue<string>();
            var stoppedOnCap = false;

            foreach (var version in versions)
            {
                var label = version?["label"]?.GetValue<string>();
                var isLabeled = !string.IsNullOrEmpty(label);

                if (!includeAutosaves && !isLabeled)
                {
                    filteredOut++;
                    continue;
                }

                if (collected.Count >= cap)
                {
                    stoppedOnCap = true;
                    break;
                }

                collected.Add((JsonNode)new JsonObject
                {
                    ["id"] = version?["id"]?.DeepClone(),
                    ["label"] = label ?? string.Empty,
                    ["description"] = version?["description"]?.GetValue<string>() ?? string.Empty,
                    ["created_at"] = version?["created_at"]?.DeepClone(),
                    ["user"] = version?["user"]?.DeepClone(),
                    ["is_labeled"] = isLabeled,
                });

                lastCollectedId = version?["id"]?.GetValue<string>();
            }

            var hasNextPage = response["pagination"]?["next_page"] is not null
                && lastReceivedId is not null
                && !string.Equals(lastReceivedId, pageCursor, StringComparison.Ordinal);

            if (stoppedOnCap)
            {
                moreAvailable = true;
                break;
            }

            if (collected.Count >= cap)
            {
                moreAvailable = hasNextPage;
                break;
            }

            if (!hasNextPage)
            {
                break;
            }

            pageCursor = lastReceivedId;
        }

        var listing = new JsonObject
        {
            ["file_key"] = fileKey,
            ["versions"] = collected,
            ["pagination"] = new JsonObject
            {
                ["next_cursor"] = moreAvailable ? lastCollectedId : null,
                ["has_more"] = moreAvailable,
                ["pages_scanned"] = pages,
            },
            ["summary"] = new JsonObject
            {
                ["returned"] = collected.Count,
                ["autosaves_skipped"] = filteredOut,
                ["api_calls_made"] = apiCalls,
            },
        };

        return listing;
    }

    /// <summary>Versions strictly older than the start, newest first — the binary search range.</summary>
    public async Task<CandidateVersions> CandidatesAsync(
        string fileKey,
        string startVersion,
        int lookback,
        bool includeAutosaves,
        CancellationToken cancellationToken)
    {
        var collected = new List<JsonObject>();
        var apiCalls = 0;
        string? cursor = null;

        var foundStart = VersionSnapshots.IsCurrent(startVersion);
        const int maxPages = 10;

        for (var page = 0; page < maxPages && collected.Count < lookback; page++)
        {
            JsonNode response;

            try
            {
                response = await PageAsync(fileKey, cursor, cancellationToken).ConfigureAwait(false);
                apiCalls++;
            }
            catch (PublishedSourceException ex)
            {
                Log.HistoryPagingStopped(logger, page, ex.Message);

                return new CandidateVersions(collected, apiCalls, ex.Message);
            }

            var versions = response["versions"] as JsonArray ?? [];

            if (versions.Count == 0)
            {
                break;
            }

            foreach (var version in versions.OfType<JsonObject>())
            {
                if (!foundStart)
                {
                    if (version["id"]?.GetValue<string>() == startVersion)
                    {
                        foundStart = true;
                    }

                    continue;
                }

                var label = version["label"]?.GetValue<string>();

                if (!includeAutosaves && string.IsNullOrEmpty(label))
                {
                    continue;
                }

                collected.Add(version);

                if (collected.Count >= lookback)
                {
                    break;
                }
            }

            if (collected.Count >= lookback || response["pagination"]?["next_page"] is null)
            {
                break;
            }

            var last = versions[^1]?["id"]?.GetValue<string>();

            if (last is null || string.Equals(last, cursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = last;
        }

        return new CandidateVersions(collected, apiCalls, Failure: null);
    }

    /// <summary>Metadata of specific versions, one history walk for all of them.</summary>
    public async Task<Dictionary<string, VersionMeta>> MetaAsync(
        string fileKey,
        IReadOnlyList<string> versionIds,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, VersionMeta>(StringComparer.Ordinal);

        if (versionIds.Count == 0)
        {
            return found;
        }

        var wanted = new HashSet<string>(versionIds, StringComparer.Ordinal);
        string? cursor = null;
        const int maxPages = 4;

        for (var page = 0; page < maxPages && found.Count < wanted.Count; page++)
        {
            JsonNode response;

            try
            {
                response = await PageAsync(fileKey, cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (PublishedSourceException ex)
            {
                Log.VersionMetadataIncomplete(logger, ex.Message);
                break;
            }

            var versions = response["versions"] as JsonArray ?? [];

            if (versions.Count == 0)
            {
                break;
            }

            foreach (var version in versions)
            {
                var id = version?["id"]?.GetValue<string>();

                if (id is null || !wanted.Contains(id) || found.ContainsKey(id))
                {
                    continue;
                }

                found[id] = new VersionMeta(
                    id,
                    version?["label"]?.GetValue<string>(),
                    version?["created_at"]?.GetValue<string>(),
                    version?["user"]?["handle"]?.GetValue<string>());
            }

            if (response["pagination"]?["next_page"] is null)
            {
                break;
            }

            var last = versions[^1]?["id"]?.GetValue<string>();

            if (last is null || string.Equals(last, cursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = last;
        }

        return found;
    }

    /// <summary>The current state described as a version, from the file it was read with.</summary>
    public static VersionMeta? HeadMeta(JsonNode? fileData) =>
        fileData is null
            ? null
            : new VersionMeta(
                fileData["version"]?.GetValue<string>() ?? VersionSnapshots.Current,
                null,
                fileData["lastModified"]?.GetValue<string>(),
                null,
                IsHead: true);

    private Task<JsonNode> PageAsync(string fileKey, string? after, CancellationToken cancellationToken) =>
        history.GetVersionsPageAsync(fileKey, FigmaPageSizeMax, after, cancellationToken);
}
