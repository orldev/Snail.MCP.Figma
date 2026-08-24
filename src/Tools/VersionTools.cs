using System.Text.Json;
using Snail.MCP.Figma.Application.Editor;
using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tools;

/// <summary>File version history via REST: list, snapshot at a version, diff, changelog and blame; past versions are cached forever, HEAD never.</summary>
[McpServerToolType]
public sealed class VersionTools(
    ILiveEditor plugin,
    IPublishedDocumentSource api,
    IDocumentHistory history,
    ActiveDocumentTracker activeFile,
    VersionSnapshotCache snapshots,
    ILogger<VersionTools> logger)
    : EditorToolBase(plugin, logger)
{
    private const string CurrentSentinel = "current";
    private const int FigmaPageSizeMax = 50;
    private const int MaxVersionsHardCap = 200;

    /// <summary>Page cap when walking history — a guard against endless loops on broken pagination.</summary>
    private const int MaxScanPages = 20;

    [McpServerTool(Name = "figma_get_file_versions")]
    [Description(ToolDescriptions.Version.GetFileVersions)]
    public Task<string> GetFileVersionsAsync(
        [Description("Link to the Figma file. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Include autosaves — versions without a label.")] bool includeAutosaves = false,
        [Description("How many versions to return, at most 200.")] int maxVersions = 50,
        [Description("Cursor from next_cursor of the previous response.")] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
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
                var response = await FetchVersionsPageAsync(fileKey, pageCursor, ct).ConfigureAwait(false);
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

                    collected.Add(new JsonObject
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

            return ToolResponse.Ok(new JsonObject
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
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_file_at_version")]
    [Description(ToolDescriptions.Version.GetFileAtVersion)]
    public Task<string> GetFileAtVersionAsync(
        [Description("Version id from figma_get_file_versions.")] string versionId,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Comma-separated node ids. Empty means the whole file.")] string? nodeIds = null,
        [Description("Tree traversal depth. Empty means unlimited.")] int? depth = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var ids = SplitIds(nodeIds);

            if (ids.Count > 0)
            {
                var nodes = await FetchNodesAtVersionAsync(fileKey, ids, versionId, depth ?? 2, ct).ConfigureAwait(false);

                return ToolResponse.Ok(new JsonObject
                {
                    ["file_key"] = fileKey,
                    ["version_id"] = versionId,
                    ["nodes"] = nodes?.DeepClone(),
                });
            }

            var document = await FetchFileAtVersionAsync(fileKey, versionId, depth, ct).ConfigureAwait(false);

            return ToolResponse.Ok(new JsonObject
            {
                ["file_key"] = fileKey,
                ["version_id"] = versionId,
                ["name"] = document?["name"]?.DeepClone(),
                ["lastModified"] = document?["lastModified"]?.DeepClone(),
                ["document"] = document?["document"]?.DeepClone(),
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_diff_versions")]
    [Description(ToolDescriptions.Version.DiffVersions)]
    public Task<string> DiffVersionsAsync(
        [Description("The earlier version to compare from.")] string fromVersion,
        [Description("The later version. 'current' is the current state.")] string toVersion,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Comma-separated node ids for the detailed comparison.")] string? componentIds = null,
        [Description("Detail level: summary, standard, detailed.")] string mode = DiffModes.Standard,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var result = await BuildDiffAsync(fileUrl, fromVersion, toVersion, componentIds, mode, ct)
                .ConfigureAwait(false);

            return ToolResponse.Ok(result.Payload);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_changes_since_version")]
    [Description(ToolDescriptions.Version.GetChangesSinceVersion)]
    public Task<string> GetChangesSinceVersionAsync(
        [Description("The version the current state is compared against.")] string sinceVersion,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Comma-separated node ids for the detailed comparison.")] string? componentIds = null,
        [Description("Detail level: summary, standard, detailed.")] string mode = DiffModes.Standard,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var result = await BuildDiffAsync(fileUrl, sinceVersion, CurrentSentinel, componentIds, mode, ct)
                .ConfigureAwait(false);

            return ToolResponse.Ok(result.Payload);
        }, cancellationToken);

    [McpServerTool(Name = "figma_generate_changelog")]
    [Description(ToolDescriptions.Version.GenerateChangelog)]
    public Task<string> GenerateChangelogAsync(
        [Description("The earlier version.")] string fromVersion,
        [Description("The later version. 'current' is the current state.")] string toVersion,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Comma-separated node ids for the per-component section.")] string? componentIds = null,
        [Description("Detail level: summary, standard, detailed.")] string mode = DiffModes.Standard,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var diff = await BuildDiffAsync(fileUrl, fromVersion, toVersion, componentIds, mode, ct)
                .ConfigureAwait(false);

            var wanted = new[] { fromVersion, toVersion }
                .Where(id => !IsCurrent(id))
                .ToList();
            var authors = await LookupVersionMetaAsync(diff.FileKey, wanted, ct).ConfigureAwait(false);

            var fromMeta = IsCurrent(fromVersion)
                ? HeadMeta(diff.HeadDocument)
                : authors.GetValueOrDefault(fromVersion);
            var toMeta = IsCurrent(toVersion)
                ? HeadMeta(diff.HeadDocument)
                : authors.GetValueOrDefault(toVersion);

            var markdown = ChangelogFormatter.Render(new ChangelogInput
            {
                FileKey = diff.FileKey,
                FileName = diff.FileName,
                FromVersionId = fromVersion,
                ToVersionId = toVersion,
                FromMeta = fromMeta,
                ToMeta = toMeta,
                PageStructure = (JsonObject)diff.Payload["page_structure"]!,
                ScopedNodes = (diff.Payload["scoped_nodes"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .ToList(),
                Notes = (diff.Payload["notes"] as JsonArray ?? [])
                    .Select(note => note?.GetValue<string>() ?? string.Empty)
                    .ToList(),
            }, DiffModes.Normalize(mode));

            var payload = (JsonObject)diff.Payload.DeepClone();
            payload["markdown"] = markdown;

            return ToolResponse.Ok(payload);
        }, cancellationToken);

    [McpServerTool(Name = "figma_blame_node")]
    [Description(ToolDescriptions.Version.BlameNode)]
    public Task<string> BlameNodeAsync(
        [Description("Node id, usually a COMPONENT_SET. Empty takes the first node of the current selection.")] string? nodeId = null,
        [Description("Key from componentPropertyDefinitions, for example 'Disabled#1:2'.")] string? targetComponentProperty = null,
        [Description("Child id — when it first appeared under the node.")] string? targetChildNodeId = null,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("The version to walk back from. The current one by default.")] string startVersion = CurrentSentinel,
        [Description("How many versions to look back through, from 2 to 500.")] int maxVersionsToWalk = 200,
        [Description("Include autosaves in the search.")] bool includeAutosaves = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var hasProperty = !string.IsNullOrWhiteSpace(targetComponentProperty);
            var hasChild = !string.IsNullOrWhiteSpace(targetChildNodeId);

            if (hasProperty == hasChild)
            {
                return ToolResponse.Error(
                    "Exactly one of targetComponentProperty and targetChildNodeId is required.",
                    "A property is looked up by a key of the form 'Disabled#1:2', a child by its node id.");
            }

            var fileKey = activeFile.Resolve(fileUrl);
            var resolvedNodeId = nodeId is null ? FirstSelectedNodeId() : NodeId.Normalize(nodeId);
            var usedSelection = nodeId is null && resolvedNodeId is not null;

            if (resolvedNodeId is null)
            {
                return ToolResponse.Error(
                    "No node was given to break down.",
                    "Pass nodeId or select a node in Figma while the plugin is running.");
            }

            var lookback = Math.Clamp(maxVersionsToWalk, 2, 500);
            var apiCalls = 0;
            var cacheHits = 0;
            var notes = new JsonArray();

            var (startNode, startCached) = await FetchNodeSnapshotAsync(fileKey, resolvedNodeId, startVersion, ct)
                .ConfigureAwait(false);

            if (startCached) cacheHits++; else apiCalls++;

            if (startNode is null)
            {
                return ToolResponse.Error(
                    $"Node {resolvedNodeId} was not found at version {startVersion}.",
                    "Check the node id and the version.");
            }

            if (!TargetExists(startNode, targetComponentProperty, targetChildNodeId))
            {
                return ToolResponse.Error(
                    "The target is absent at the start version — so it has not appeared yet or was already deleted.",
                    "Check the property key or the child id, or take a later startVersion.");
            }

            var (candidates, listCalls, listFailed, listError) =
                await CollectCandidateVersionsAsync(fileKey, startVersion, lookback, includeAutosaves, ct)
                    .ConfigureAwait(false);
            apiCalls += listCalls;

            if (listFailed && candidates.Count == 0)
            {
                return ToolResponse.Error(
                    $"The version list is unavailable: {listError ?? "the reason is unknown"}. " +
                    "There is nothing to attribute the edit to — no history arrived at all.",
                    "Retry the request or check that the token has the file_versions:read scope (the Versions Read permission on a PAT).");
            }

            var low = 0;
            var high = candidates.Count - 1;
            var probes = 0;
            var oldestExistsIndex = -1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var versionId = candidates[middle]["id"]?.GetValue<string>() ?? string.Empty;

                var (node, cached) = await FetchNodeSnapshotAsync(fileKey, resolvedNodeId, versionId, ct)
                    .ConfigureAwait(false);

                if (cached) cacheHits++; else apiCalls++;

                probes++;

                if (node is not null && TargetExists(node, targetComponentProperty, targetChildNodeId))
                {
                    oldestExistsIndex = Math.Max(oldestExistsIndex, middle);
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            JsonObject introduced;
            string certainty;

            if (oldestExistsIndex < 0)
            {
                var lookupId = startVersion;

                if (IsCurrent(startVersion))
                {
                    var (headFile, headCached) = await FetchFileSnapshotAsync(fileKey, CurrentSentinel, ct).ConfigureAwait(false);

                    if (headCached) cacheHits++; else apiCalls++;

                    lookupId = headFile?["version"]?.GetValue<string>() ?? startVersion;
                }

                var meta = (await LookupVersionMetaAsync(fileKey, [lookupId], ct).ConfigureAwait(false))
                    .GetValueOrDefault(lookupId);
                apiCalls++;

                introduced = new JsonObject
                {
                    ["version_id"] = lookupId,
                    ["label"] = meta?.Label,
                    ["created_at"] = meta?.CreatedAt,
                    ["user_handle"] = meta?.UserHandle,
                    ["is_labeled"] = !string.IsNullOrEmpty(meta?.Label),
                };

                certainty = meta?.UserHandle switch
                {
                    "Figma" => "system_attributed",
                    { Length: > 0 } => "exact",
                    _ => "metadata_unavailable",
                };

                if (certainty == "metadata_unavailable")
                {
                    notes.Add(
                        "The edit is present at the very start version, but its author was not found in the history walked. " +
                        "The appearance itself is confirmed, there is nothing in the REST data to attribute a person by.");
                }
            }
            else
            {
                var version = candidates[oldestExistsIndex];
                var handle = version["user"]?["handle"]?.GetValue<string>();
                var label = version["label"]?.GetValue<string>();

                introduced = new JsonObject
                {
                    ["version_id"] = version["id"]?.DeepClone(),
                    ["label"] = string.IsNullOrEmpty(label) ? null : label,
                    ["created_at"] = version["created_at"]?.DeepClone(),
                    ["user_handle"] = handle,
                    ["is_labeled"] = !string.IsNullOrEmpty(label),
                };

                if (oldestExistsIndex == candidates.Count - 1)
                {
                    certainty = "exists_at_lookback_horizon";
                    notes.Add(
                        $"The target is present even at the oldest version walked ({version["id"]}). " +
                        $"The real appearance lies deeper: raise maxVersionsToWalk (currently {lookback}).");
                }
                else if (handle == "Figma")
                {
                    certainty = "system_attributed";
                    notes.Add(
                        "The version of appearance is a system autosave (the Figma user). " +
                        "To find a person set includeAutosaves=false: the search will then land on the nearest named version.");
                }
                else
                {
                    certainty = "exact";
                }
            }

            if (listFailed)
            {
                certainty = DowngradeCertainty(certainty);
                notes.Add(
                    $"Walking the history broke off halfway ({listError ?? "the reason is unknown"}); only " +
                    $"{candidates.Count} versions were walked. The real appearance may lie outside them, so the certainty is lowered one step.");
            }

            notes.Add(
                "The binary search assumes the existence of the target is monotonic: added once and never deleted again. " +
                "If the edit was added, removed and brought back, the version found may not be the first one.");

            if (usedSelection)
            {
                notes.Add($"Node {resolvedNodeId} was taken from the current selection. Pass nodeId to set it explicitly.");
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["file_key"] = fileKey,
                ["node_id"] = resolvedNodeId,
                ["target"] = hasProperty
                    ? new JsonObject { ["type"] = "component_property", ["name"] = targetComponentProperty }
                    : new JsonObject { ["type"] = "child_node", ["node_id"] = targetChildNodeId },
                ["introduced_at"] = introduced,
                ["attribution_certainty"] = certainty,
                ["summary"] = new JsonObject
                {
                    ["versions_in_search_range"] = candidates.Count,
                    ["probes_made"] = probes,
                    ["used_selection"] = usedSelection,
                    ["api_calls_made"] = apiCalls,
                    ["cache_hits"] = cacheHits,
                },
                ["notes"] = notes,
            });
        }, cancellationToken);

    private sealed record DiffOutcome(string FileKey, string? FileName, JsonObject Payload, JsonNode? HeadDocument);

    private async Task<DiffOutcome> BuildDiffAsync(
        string? fileUrl,
        string fromVersion,
        string toVersion,
        string? componentIds,
        string mode,
        CancellationToken cancellationToken)
    {
        var fileKey = activeFile.Resolve(fileUrl);
        var normalizedMode = DiffModes.Normalize(mode);
        var notes = new JsonArray();
        var apiCalls = 0;
        var cacheHits = 0;

        var (fromFile, fromCached) = await FetchFileSnapshotAsync(fileKey, fromVersion, cancellationToken).ConfigureAwait(false);

        if (fromCached) cacheHits++; else apiCalls++;

        var (toFile, toCached) = await FetchFileSnapshotAsync(fileKey, toVersion, cancellationToken).ConfigureAwait(false);

        if (toCached) cacheHits++; else apiCalls++;

        var pageStructure = DiffEngine.DiffPageStructure(fromFile?["document"], toFile?["document"]);

        var ids = SplitIds(componentIds);
        var usedSelection = false;

        if (ids.Count == 0)
        {
            ids = SelectedNodeIds();
            usedSelection = ids.Count > 0;

            if (usedSelection)
            {
                notes.Add(
                    $"The detailed comparison was done over the current selection ({ids.Count} nodes). " +
                    "Pass componentIds to set the list explicitly.");
            }
            else
            {
                notes.Add(
                    "componentIds was not passed and there is no selection — only the page structure was compared. " +
                    "figma_get_design_system_kit or figma_search_components provide the node ids.");
            }
        }

        var scoped = new JsonArray();

        foreach (var id in ids)
        {
            var (fromNode, fromNodeCached) = await FetchNodeSnapshotAsync(fileKey, id, fromVersion, cancellationToken).ConfigureAwait(false);

            if (fromNodeCached) cacheHits++; else apiCalls++;

            var (toNode, toNodeCached) = await FetchNodeSnapshotAsync(fileKey, id, toVersion, cancellationToken).ConfigureAwait(false);

            if (toNodeCached) cacheHits++; else apiCalls++;

            var nodeDiff = DiffEngine.DiffNode(fromNode, toNode, normalizedMode);
            AttachMetadataChanges(nodeDiff, id, fromFile, toFile);
            scoped.Add(nodeDiff);
        }

        var payload = new JsonObject
        {
            ["file_key"] = fileKey,
            ["file_name"] = toFile?["name"]?.DeepClone(),
            ["from_version_id"] = fromVersion,
            ["to_version_id"] = toVersion,
            ["mode"] = normalizedMode,
            ["page_structure"] = pageStructure,
            ["scoped_nodes"] = scoped,
            ["notes"] = notes,
            ["summary"] = new JsonObject
            {
                ["scoped_node_count"] = scoped.Count,
                ["used_selection"] = usedSelection,
                ["api_calls_made"] = apiCalls,
                ["cache_hits"] = cacheHits,
            },
        };

        return new DiffOutcome(fileKey, toFile?["name"]?.GetValue<string>(), payload, IsCurrent(toVersion) ? toFile : null);
    }

    /// <summary>Description and annotation edits are absent from REST snapshots; the plugin event buffer is the only source.</summary>
    private void AttachMetadataChanges(JsonObject nodeDiff, string nodeId, JsonNode? fromFile, JsonNode? toFile)
    {
        var session = Plugin.Documents.Resolve();

        if (session is null)
        {
            return;
        }

        var from = ParseTimestamp(fromFile?["lastModified"]?.GetValue<string>());
        var to = ParseTimestamp(toFile?["lastModified"]?.GetValue<string>()) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var changes = new JsonArray();

        foreach (var change in session.MetadataChanges.Tail(200))
        {
            if (change["nodeId"]?.GetValue<string>() != nodeId)
            {
                continue;
            }

            var timestamp = change["receivedAt"]?.GetValue<long>() ?? 0;

            if (from is not null && timestamp < from)
            {
                continue;
            }

            if (timestamp > to)
            {
                continue;
            }

            changes.Add(new JsonObject
            {
                ["field"] = change["field"]?.DeepClone() ?? "description",
                ["new_value"] = change["value"]?.DeepClone(),
                ["timestamp"] = timestamp,
                ["source"] = "plugin_buffer",
            });
        }

        if (changes.Count == 0)
        {
            return;
        }

        nodeDiff["metadata_changes"] = changes;
        nodeDiff["change_count"] = (nodeDiff["change_count"]?.GetValue<int>() ?? 0) + changes.Count;
    }

    private static long? ParseTimestamp(string? iso) =>
        DateTimeOffset.TryParse(iso, out var value) ? value.ToUnixTimeMilliseconds() : null;

    /// <summary>File snapshot at a version, depth 1; HEAD is never cached.</summary>
    private async Task<(JsonNode? File, bool Cached)> FetchFileSnapshotAsync(
        string fileKey,
        string versionId,
        CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, 1, null);

        if (snapshots.Get(key) is { } cached)
        {
            return (cached, true);
        }

        var file = await api.GetDocumentAsync(
            fileKey,
            new DocumentReadOptions { Depth = 1, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, file);

        return (file, false);
    }

    /// <summary>Node snapshot at a version, depth 2.</summary>
    private async Task<(JsonNode? Node, bool Cached)> FetchNodeSnapshotAsync(
        string fileKey,
        string nodeId,
        string versionId,
        CancellationToken cancellationToken)
    {
        var isHead = IsCurrent(versionId);
        var key = isHead ? null : VersionSnapshotCache.MakeKey(fileKey, versionId, 2, [nodeId]);

        if (snapshots.Get(key) is { } cached)
        {
            return (cached["nodes"]?[nodeId]?["document"], true);
        }

        var response = await api.GetNodesAsync(
            fileKey,
            [nodeId],
            new NodeReadOptions { Depth = 2, Version = isHead ? null : versionId },
            cancellationToken).ConfigureAwait(false);

        snapshots.Set(key, response);

        return (response["nodes"]?[nodeId]?["document"], false);
    }

    private async Task<JsonNode?> FetchFileAtVersionAsync(
        string fileKey,
        string versionId,
        int? depth,
        CancellationToken cancellationToken)
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

    private async Task<JsonNode?> FetchNodesAtVersionAsync(
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

    private Task<JsonNode> FetchVersionsPageAsync(string fileKey, string? after, CancellationToken cancellationToken) =>
        history.GetVersionsPageAsync(fileKey, FigmaPageSizeMax, after, cancellationToken);

    /// <summary>Versions strictly older than the start, newest first — the binary search range.</summary>
    private async Task<(List<JsonObject> Versions, int ApiCalls, bool Failed, string? Error)> CollectCandidateVersionsAsync(
        string fileKey,
        string startVersion,
        int lookback,
        bool includeAutosaves,
        CancellationToken cancellationToken)
    {
        var collected = new List<JsonObject>();
        var apiCalls = 0;
        string? cursor = null;

        var foundStart = IsCurrent(startVersion);
        const int maxPages = 10;

        for (var page = 0; page < maxPages && collected.Count < lookback; page++)
        {
            JsonNode response;

            try
            {
                response = await FetchVersionsPageAsync(fileKey, cursor, cancellationToken).ConfigureAwait(false);
                apiCalls++;
            }
            catch (PublishedSourceException ex)
            {
                Logger.LogWarning("History paging stopped at page {Page}: {Message}", page, ex.Message);

                return (collected, apiCalls, true, ex.Message);
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

        return (collected, apiCalls, false, null);
    }

    /// <summary>Metadata of specific versions, one history walk for all of them.</summary>
    private async Task<Dictionary<string, VersionMeta>> LookupVersionMetaAsync(
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
                response = await FetchVersionsPageAsync(fileKey, cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (PublishedSourceException ex)
            {
                Logger.LogWarning("Version metadata could not be read fully: {Message}", ex.Message);
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

    private static VersionMeta? HeadMeta(JsonNode? fileData) =>
        fileData is null
            ? null
            : new VersionMeta(
                fileData["version"]?.GetValue<string>() ?? CurrentSentinel,
                null,
                fileData["lastModified"]?.GetValue<string>(),
                null,
                IsHead: true);

    private static bool IsCurrent(string versionId) =>
        string.Equals(versionId, CurrentSentinel, StringComparison.OrdinalIgnoreCase);

    private static bool TargetExists(JsonNode node, string? componentProperty, string? childNodeId)
    {
        if (!string.IsNullOrWhiteSpace(componentProperty))
        {
            return node["componentPropertyDefinitions"]?[componentProperty] is not null;
        }

        return !string.IsNullOrWhiteSpace(childNodeId) && ContainsChild(node, NodeId.Normalize(childNodeId));
    }

    private static bool ContainsChild(JsonNode? node, string targetId)
    {
        if (node is null)
        {
            return false;
        }

        if (node["id"]?.GetValue<string>() == targetId)
        {
            return true;
        }

        return node["children"] is JsonArray children
            && children.Any(child => ContainsChild(child, targetId));
    }

    /// <summary>Lowers confidence one step when the scanned range is known to be incomplete.</summary>
    private static string DowngradeCertainty(string certainty) => certainty switch
    {
        "exact" or "system_attributed" => "exists_at_lookback_horizon",
        "exists_at_lookback_horizon" => "metadata_unavailable",
        _ => certainty,
    };

    private static List<string> SplitIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? []
            : [.. ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(NodeId.Normalize)];

    private List<string> SelectedNodeIds()
    {
        var nodes = Plugin.Documents.Resolve()?.Selection?["nodes"] as JsonArray ?? [];

        return [.. nodes
            .Select(node => node?["id"]?.GetValue<string>())
            .OfType<string>()];
    }

    private string? FirstSelectedNodeId() => SelectedNodeIds().FirstOrDefault();

    [McpServerTool(Name = "figma_save_version")]
    [Description(ToolDescriptions.Version.SaveVersion)]
    public Task<string> SaveVersionAsync(
        [Description("Title of the version checkpoint.")] string title,
        [Description("Optional description shown in the history panel.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return ToolResponse.Error("title is empty.", "Name the checkpoint after the finished batch of work.");
            }

            var script = $$"""
                const result = await figma.saveVersionHistoryAsync(
                    {{JsonSerializer.Serialize(title)}}{{(description is null ? "" : $", {JsonSerializer.Serialize(description)}")}});
                return { versionId: result && result.id ? result.id : null, title: {{JsonSerializer.Serialize(title)}} };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 20000 },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            return ToolResponse.Ok(payload ?? new JsonObject(), envelope =>
            {
                envelope["hint"] = "The id anchors figma_diff_versions and figma_get_changes_since_version.";
            });
        }, cancellationToken);
}
