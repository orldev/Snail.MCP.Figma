namespace Snail.MCP.Figma.Application.Diff;

/// <summary>What to attribute: a component property or a child of a node, searched back from a version.</summary>
public sealed record BlameRequest(
    string FileKey,
    string? NodeId,
    bool IsNodeFromSelection,
    string? TargetComponentProperty,
    string? TargetChildNodeId,
    string StartVersion,
    int MaxVersionsToWalk,
    bool IncludesAutosaves);

/// <summary>The version an edit appeared in, or why it could not be found — a failure the caller can act on, carried as data.</summary>
public sealed record BlameAnswer(JsonObject? Payload, string? Error = null, string? Hint = null)
{
    /// <summary>Answer without a payload, carrying the failure and what the caller can do about it.</summary>
    public static BlameAnswer Fail(string error, string hint) => new(null, error, hint);
}

/// <summary>Which version brought a property or a child into a node: a binary search over the history, two REST reads a step.</summary>
public sealed class NodeBlame(VersionSnapshots snapshots, VersionLog log)
{
    /// <summary>The version the target first appeared in, with how certain the attribution is.</summary>
    public async Task<BlameAnswer> BlameAsync(BlameRequest request, CancellationToken cancellationToken)
    {
        var hasProperty = !string.IsNullOrWhiteSpace(request.TargetComponentProperty);
        var hasChild = !string.IsNullOrWhiteSpace(request.TargetChildNodeId);

        if (hasProperty == hasChild)
        {
            return BlameAnswer.Fail(
                "Exactly one of targetComponentProperty and targetChildNodeId is required.",
                "A property is looked up by a key of the form 'Disabled#1:2', a child by its node id.");
        }

        if (request.NodeId is not { } nodeId)
        {
            return BlameAnswer.Fail(
                "No node was given to break down.",
                "Pass nodeId or select a node in Figma while the plugin is running.");
        }

        var lookback = Math.Clamp(request.MaxVersionsToWalk, 2, 500);
        var cost = new Cost();
        var notes = new JsonArray();

        var start = await snapshots.NodeAsync(request.FileKey, nodeId, request.StartVersion, cancellationToken).ConfigureAwait(false);
        cost.Count(start);

        if (start.Node is null)
        {
            return BlameAnswer.Fail(
                $"Node {nodeId} was not found at version {request.StartVersion}.",
                "Check the node id and the version.");
        }

        if (!HasTarget(start.Node, request))
        {
            return BlameAnswer.Fail(
                "The target is absent at the start version — so it has not appeared yet or was already deleted.",
                "Check the property key or the child id, or take a later startVersion.");
        }

        var candidates = await log.CandidatesAsync(request.FileKey, request.StartVersion, lookback, request.IncludesAutosaves, cancellationToken)
            .ConfigureAwait(false);
        cost.ApiCalls += candidates.ApiCalls;

        if (candidates.IsBroken && candidates.Versions.Count == 0)
        {
            return BlameAnswer.Fail(
                $"The version list is unavailable: {candidates.Failure}. " +
                "There is nothing to attribute the edit to — no history arrived at all.",
                "Retry the request or check that the token has the file_versions:read scope (the Versions Read permission on a PAT).");
        }

        var (oldestExistsIndex, probes) = await SearchAsync(request, nodeId, candidates.Versions, cost, cancellationToken).ConfigureAwait(false);

        var (introduced, certainty) = oldestExistsIndex < 0
            ? await AtStartAsync(request, cost, notes, cancellationToken).ConfigureAwait(false)
            : AtCandidate(candidates.Versions, oldestExistsIndex, lookback, notes);

        if (candidates.IsBroken)
        {
            certainty = DowngradeCertainty(certainty);
            notes.Add(
                (JsonNode)($"Walking the history broke off halfway ({candidates.Failure}); only " +
                $"{candidates.Versions.Count} versions were walked. The real appearance may lie outside them, so the certainty is lowered one step."));
        }

        notes.Add(
            (JsonNode)("The binary search assumes the existence of the target is monotonic: added once and never deleted again. " +
            "If the edit was added, removed and brought back, the version found may not be the first one."));

        if (request.IsNodeFromSelection)
        {
            notes.Add((JsonNode)$"Node {nodeId} was taken from the current selection. Pass nodeId to set it explicitly.");
        }

        var target = hasProperty
            ? new JsonObject { ["type"] = "component_property", ["name"] = request.TargetComponentProperty }
            : new JsonObject { ["type"] = "child_node", ["node_id"] = request.TargetChildNodeId };

        var payload = new JsonObject
        {
            ["file_key"] = request.FileKey,
            ["node_id"] = nodeId,
            ["target"] = target,
            ["introduced_at"] = introduced,
            ["attribution_certainty"] = certainty,
            ["summary"] = new JsonObject
            {
                ["versions_in_search_range"] = candidates.Versions.Count,
                ["probes_made"] = probes,
                ["used_selection"] = request.IsNodeFromSelection,
                ["api_calls_made"] = cost.ApiCalls,
                ["cache_hits"] = cost.CacheHits,
            },
            ["notes"] = notes,
        };

        return new BlameAnswer(payload);
    }

    /// <summary>The binary search: the oldest candidate that still has the target, or -1 when none has.</summary>
    private async Task<(int OldestExistsIndex, int Probes)> SearchAsync(
        BlameRequest request,
        string nodeId,
        IReadOnlyList<JsonObject> candidates,
        Cost cost,
        CancellationToken cancellationToken)
    {
        var low = 0;
        var high = candidates.Count - 1;
        var probes = 0;
        var oldestExistsIndex = -1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var versionId = candidates[middle]["id"]?.GetValue<string>() ?? string.Empty;

            var probe = await snapshots.NodeAsync(request.FileKey, nodeId, versionId, cancellationToken).ConfigureAwait(false);
            cost.Count(probe);
            probes++;

            if (probe.Node is not null && HasTarget(probe.Node, request))
            {
                oldestExistsIndex = Math.Max(oldestExistsIndex, middle);
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return (oldestExistsIndex, probes);
    }

    /// <summary>No older version has the target, so the start version brought it; its author comes from the version list.</summary>
    private async Task<(JsonObject Introduced, string Certainty)> AtStartAsync(
        BlameRequest request,
        Cost cost,
        JsonArray notes,
        CancellationToken cancellationToken)
    {
        var lookupId = request.StartVersion;

        if (VersionSnapshots.IsCurrent(request.StartVersion))
        {
            var head = await snapshots.FileAsync(request.FileKey, VersionSnapshots.Current, cancellationToken).ConfigureAwait(false);
            cost.Count(head);

            lookupId = head.Node?["version"]?.GetValue<string>() ?? request.StartVersion;
        }

        var meta = (await log.MetaAsync(request.FileKey, [lookupId], cancellationToken).ConfigureAwait(false))
            .GetValueOrDefault(lookupId);
        cost.ApiCalls++;

        var introduced = new JsonObject
        {
            ["version_id"] = lookupId,
            ["label"] = meta?.Label,
            ["created_at"] = meta?.CreatedAt,
            ["user_handle"] = meta?.UserHandle,
            ["is_labeled"] = !string.IsNullOrEmpty(meta?.Label),
        };

        var certainty = meta?.UserHandle switch
        {
            "Figma" => "system_attributed",
            { Length: > 0 } => "exact",
            _ => "metadata_unavailable",
        };

        if (certainty == "metadata_unavailable")
        {
            notes.Add(
                (JsonNode)("The edit is present at the very start version, but its author was not found in the history walked. " +
                "The appearance itself is confirmed, there is nothing in the REST data to attribute a person by."));
        }

        return (introduced, certainty);
    }

    /// <summary>The oldest candidate with the target brought it, unless it is the last one walked and the truth lies deeper.</summary>
    private static (JsonObject Introduced, string Certainty) AtCandidate(
        IReadOnlyList<JsonObject> candidates,
        int oldestExistsIndex,
        int lookback,
        JsonArray notes)
    {
        var version = candidates[oldestExistsIndex];
        var handle = version["user"]?["handle"]?.GetValue<string>();
        var label = version["label"]?.GetValue<string>();

        var introduced = new JsonObject
        {
            ["version_id"] = version["id"]?.DeepClone(),
            ["label"] = string.IsNullOrEmpty(label) ? null : label,
            ["created_at"] = version["created_at"]?.DeepClone(),
            ["user_handle"] = handle,
            ["is_labeled"] = !string.IsNullOrEmpty(label),
        };

        if (oldestExistsIndex == candidates.Count - 1)
        {
            notes.Add(
                (JsonNode)($"The target is present even at the oldest version walked ({version["id"]}). " +
                $"The real appearance lies deeper: raise maxVersionsToWalk (currently {lookback})."));

            return (introduced, "exists_at_lookback_horizon");
        }

        if (handle == "Figma")
        {
            notes.Add(
                (JsonNode)("The version of appearance is a system autosave (the Figma user). " +
                "To find a person set includeAutosaves=false: the search will then land on the nearest named version."));

            return (introduced, "system_attributed");
        }

        return (introduced, "exact");
    }

    private static bool HasTarget(JsonNode node, BlameRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetComponentProperty))
        {
            return node["componentPropertyDefinitions"]?[request.TargetComponentProperty] is not null;
        }

        return !string.IsNullOrWhiteSpace(request.TargetChildNodeId) && ContainsChild(node, NodeId.Normalize(request.TargetChildNodeId));
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

    /// <summary>What the search spent: requests sent to Figma and reads the cache answered.</summary>
    private sealed class Cost
    {
        public int ApiCalls { get; set; }

        public int CacheHits { get; set; }

        public void Count(VersionSnapshot snapshot)
        {
            if (snapshot.IsCached)
            {
                CacheHits++;
            }
            else
            {
                ApiCalls++;
            }
        }
    }
}
