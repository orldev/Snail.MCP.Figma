namespace Snail.MCP.Figma.Application.Diff;

/// <summary>What to compare: two versions of a file, the nodes named or else the ones selected, and the plugin's buffer of metadata edits when a plugin is there.</summary>
public sealed record DiffRequest(
    string FileKey,
    string FromVersion,
    string ToVersion,
    IReadOnlyList<string> ComponentIds,
    IReadOnlyList<string> SelectedIds,
    string Mode,
    RingBuffer<JsonNode>? MetadataChanges);

/// <summary>A comparison of two versions, with the current file when one side was the current state.</summary>
public sealed record VersionDiff(string FileKey, string? FileName, JsonObject Payload, JsonNode? HeadDocument);

/// <summary>Two versions of a file compared: the page structure always, the named nodes in detail, and a changelog over both.</summary>
public sealed class VersionComparison(VersionSnapshots snapshots, VersionLog log)
{
    /// <summary>The comparison of the two versions; nodes are compared over the ones named, or the selection when none are.</summary>
    public async Task<VersionDiff> DiffAsync(DiffRequest request, CancellationToken cancellationToken)
    {
        var normalizedMode = DiffModes.Normalize(request.Mode);
        var notes = new JsonArray();
        var apiCalls = 0;
        var cacheHits = 0;

        var fromFile = await snapshots.FileAsync(request.FileKey, request.FromVersion, cancellationToken).ConfigureAwait(false);
        Count(fromFile, ref apiCalls, ref cacheHits);

        var toFile = await snapshots.FileAsync(request.FileKey, request.ToVersion, cancellationToken).ConfigureAwait(false);
        Count(toFile, ref apiCalls, ref cacheHits);

        var pageStructure = DiffEngine.DiffPageStructure(fromFile.Node?["document"], toFile.Node?["document"]);
        var (ids, usedSelection) = ScopeOf(request, notes);
        var scoped = new JsonArray();

        foreach (var id in ids)
        {
            var fromNode = await snapshots.NodeAsync(request.FileKey, id, request.FromVersion, cancellationToken).ConfigureAwait(false);
            Count(fromNode, ref apiCalls, ref cacheHits);

            var toNode = await snapshots.NodeAsync(request.FileKey, id, request.ToVersion, cancellationToken).ConfigureAwait(false);
            Count(toNode, ref apiCalls, ref cacheHits);

            var nodeDiff = DiffEngine.DiffNode(fromNode.Node, toNode.Node, normalizedMode);
            AttachMetadataChanges(nodeDiff, id, fromFile.Node, toFile.Node, request.MetadataChanges);
            scoped.Add((JsonNode)nodeDiff);
        }

        var payload = new JsonObject
        {
            ["file_key"] = request.FileKey,
            ["file_name"] = toFile.Node?["name"]?.DeepClone(),
            ["from_version_id"] = request.FromVersion,
            ["to_version_id"] = request.ToVersion,
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

        var headDocument = VersionSnapshots.IsCurrent(request.ToVersion) ? toFile.Node : null;

        return new VersionDiff(request.FileKey, toFile.Node?["name"]?.GetValue<string>(), payload, headDocument);
    }

    /// <summary>The comparison together with a Markdown changelog naming who saved each version.</summary>
    public async Task<JsonObject> ChangelogAsync(DiffRequest request, CancellationToken cancellationToken)
    {
        var diff = await DiffAsync(request, cancellationToken).ConfigureAwait(false);

        var wanted = new[] { request.FromVersion, request.ToVersion }
            .Where(id => !VersionSnapshots.IsCurrent(id))
            .ToList();
        var authors = await log.MetaAsync(diff.FileKey, wanted, cancellationToken).ConfigureAwait(false);

        var fromMeta = VersionSnapshots.IsCurrent(request.FromVersion)
            ? VersionLog.HeadMeta(diff.HeadDocument)
            : authors.GetValueOrDefault(request.FromVersion);
        var toMeta = VersionSnapshots.IsCurrent(request.ToVersion)
            ? VersionLog.HeadMeta(diff.HeadDocument)
            : authors.GetValueOrDefault(request.ToVersion);

        var input = new ChangelogInput
        {
            FileKey = diff.FileKey,
            FileName = diff.FileName,
            FromVersionId = request.FromVersion,
            ToVersionId = request.ToVersion,
            FromMeta = fromMeta,
            ToMeta = toMeta,
            PageStructure = (JsonObject)diff.Payload["page_structure"]!,
            ScopedNodes = [.. (diff.Payload["scoped_nodes"] as JsonArray ?? []).OfType<JsonObject>()],
            Notes = [.. (diff.Payload["notes"] as JsonArray ?? []).Select(note => note?.GetValue<string>() ?? string.Empty)],
        };
        var markdown = ChangelogFormatter.Render(input, DiffModes.Normalize(request.Mode));

        var payload = (JsonObject)diff.Payload.DeepClone();
        payload["markdown"] = markdown;

        return payload;
    }

    /// <summary>The nodes to compare in detail and whether they came from the selection; the note says which, or that only the pages were compared.</summary>
    private static (IReadOnlyList<string> Ids, bool UsedSelection) ScopeOf(DiffRequest request, JsonArray notes)
    {
        if (request.ComponentIds.Count > 0)
        {
            return (request.ComponentIds, false);
        }

        if (request.SelectedIds.Count > 0)
        {
            notes.Add(
                (JsonNode)($"The detailed comparison was done over the current selection ({request.SelectedIds.Count} nodes). " +
                "Pass componentIds to set the list explicitly."));

            return (request.SelectedIds, true);
        }

        notes.Add(
            (JsonNode)("componentIds was not passed and there is no selection — only the page structure was compared. " +
            "figma_get_design_system_kit or figma_search_components provide the node ids."));

        return ([], false);
    }

    private static void Count(VersionSnapshot snapshot, ref int apiCalls, ref int cacheHits)
    {
        if (snapshot.IsCached)
        {
            cacheHits++;
        }
        else
        {
            apiCalls++;
        }
    }

    /// <summary>Description and annotation edits are absent from REST snapshots; the plugin event buffer is the only source.</summary>
    private static void AttachMetadataChanges(
        JsonObject nodeDiff,
        string nodeId,
        JsonNode? fromFile,
        JsonNode? toFile,
        RingBuffer<JsonNode>? metadataChanges)
    {
        if (metadataChanges is null)
        {
            return;
        }

        var from = ParseTimestamp(fromFile?["lastModified"]?.GetValue<string>());
        var to = ParseTimestamp(toFile?["lastModified"]?.GetValue<string>()) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var changes = new JsonArray();

        foreach (var change in metadataChanges.Tail(200))
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

            changes.Add((JsonNode)new JsonObject
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
}
