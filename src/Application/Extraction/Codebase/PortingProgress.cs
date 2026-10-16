namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>How far the port of the design system has come, component by component, in the progress manifest beside the analysis.</summary>
public static class PortingProgress
{
    /// <summary>Taken around every read and write of a progress manifest.</summary>
    /// <remarks>A shared server answers several agents at once, and two of them reporting progress read the same manifest and each wrote back its own copy: the first report was lost.</remarks>
    private static readonly Lock StatusGate = new();

    /// <summary>The progress of the port, after recording the component's new status when one is given.</summary>
    public static ExtractionAnswer Update(string outDir, string? component, string? status, string? notes, string? storyFile)
    {
        lock (StatusGate)
        {
            return UpdateUnderGate(outDir, component, status, notes, storyFile);
        }
    }

    private static ExtractionAnswer UpdateUnderGate(string outDir, string? component, string? status, string? notes, string? storyFile)
    {
        var resolvedOut = ExtractionManifests.ResolvePath(outDir);
        var statusPath = ExtractionManifests.StatusPath(resolvedOut);
        var manifest = ExtractionManifests.Read<DsStatusManifest>(statusPath);

        if (manifest is null)
        {
            var analysis = ExtractionManifests.Read<DsAnalysis>(ExtractionManifests.AnalysisPath(resolvedOut));

            if (analysis is null)
            {
                return ExtractionAnswer.Fail(
                    $"Neither a progress manifest nor an analysis manifest exists in {ExtractionManifests.Directory(resolvedOut)}.",
                    "Run figma_ds_analyze with the same outDir first.");
            }

            manifest = ProjectAnalyzer.InitStatus(analysis);
        }

        if (!string.IsNullOrWhiteSpace(component))
        {
            if (string.IsNullOrWhiteSpace(status) || !DsPortStatuses.All.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return ExtractionAnswer.Fail(
                    $"Status '{status}' is not in the list.",
                    $"Allowed: {string.Join(", ", DsPortStatuses.All)}.");
            }

            var now = DateTimeOffset.UtcNow.ToString("O");
            manifest.Components[component] = new DsStatusEntry
            {
                Status = status.ToLowerInvariant(),
                Notes = notes,
                StoryFile = storyFile,
                UpdatedAt = now,
            };
            manifest.UpdatedAt = now;
            ExtractionManifests.Write(statusPath, manifest);
        }

        var counts = new JsonObject();

        foreach (var group in manifest.Components.Values.GroupBy(e => e.Status, StringComparer.Ordinal))
        {
            counts[group.Key] = group.Count();
        }

        var remaining = manifest.Components
            .Where(pair => pair.Value.Status is DsPortStatuses.Pending or DsPortStatuses.InProgress)
            .Take(25)
            .Select(pair => (JsonNode)new JsonObject
            {
                ["name"] = pair.Key,
                ["status"] = pair.Value.Status,
            })
            .ToArray();

        return ExtractionAnswer.Ok(new JsonObject
        {
            ["statusPath"] = statusPath,
            ["counts"] = counts,
            ["remaining"] = new JsonArray(remaining),
            ["updated"] = string.IsNullOrWhiteSpace(component) ? null : component,
        });
    }
}
