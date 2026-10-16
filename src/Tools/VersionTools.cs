using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Diff;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>File version history via REST: list, snapshot at a version, diff, changelog and blame; past versions are cached forever, HEAD never.</summary>
[McpServerToolType]
public sealed class VersionTools(
    ILiveEditor plugin,
    VersionLog log,
    VersionSnapshots snapshots,
    VersionComparison comparison,
    NodeBlame blame,
    ActiveDocumentTracker activeFile,
    ILogger<VersionTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Lists the version history of the file, named versions only unless autosaves are asked for.</summary>
    [McpServerTool(Name = "figma_get_file_versions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Version.GetFileVersions)]
    public Task<string> GetFileVersionsAsync(
        [Description("Link to the Figma file. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Include autosaves — versions without a label.")] bool includeAutosaves = false,
        [Description("How many versions to return, at most 200.")] int maxVersions = 50,
        [Description("Cursor from next_cursor of the previous response.")] string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var listing = await log.ListAsync(activeFile.Resolve(fileUrl), includeAutosaves, maxVersions, cursor, ct).ConfigureAwait(false);

            return ToolResponse.Ok(listing);
        }, cancellationToken);

    /// <summary>Returns the file or chosen nodes as they were at a past version.</summary>
    [McpServerTool(Name = "figma_get_file_at_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
                var nodes = await snapshots.NodesAtAsync(fileKey, ids, versionId, depth ?? 2, ct).ConfigureAwait(false);

                return ToolResponse.Ok(new JsonObject
                {
                    ["file_key"] = fileKey,
                    ["version_id"] = versionId,
                    ["nodes"] = nodes?.DeepClone(),
                });
            }

            var document = await snapshots.FileAtAsync(fileKey, versionId, depth, ct).ConfigureAwait(false);

            return ToolResponse.Ok(new JsonObject
            {
                ["file_key"] = fileKey,
                ["version_id"] = versionId,
                ["name"] = document?["name"]?.DeepClone(),
                ["lastModified"] = document?["lastModified"]?.DeepClone(),
                ["document"] = document?["document"]?.DeepClone(),
            });
        }, cancellationToken);

    /// <summary>Compares the page structure of two versions and, for chosen nodes, their details.</summary>
    [McpServerTool(Name = "figma_diff_versions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
            var diff = await comparison.DiffAsync(Diff(fileUrl, fromVersion, toVersion, componentIds, mode), ct).ConfigureAwait(false);

            return ToolResponse.Ok(diff.Payload);
        }, cancellationToken);

    /// <summary>Compares a past version against the current state of the file.</summary>
    [McpServerTool(Name = "figma_get_changes_since_version", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Version.GetChangesSinceVersion)]
    public Task<string> GetChangesSinceVersionAsync(
        [Description("The version the current state is compared against.")] string sinceVersion,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("Comma-separated node ids for the detailed comparison.")] string? componentIds = null,
        [Description("Detail level: summary, standard, detailed.")] string mode = DiffModes.Standard,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var diff = await comparison.DiffAsync(Diff(fileUrl, sinceVersion, VersionSnapshots.Current, componentIds, mode), ct).ConfigureAwait(false);

            return ToolResponse.Ok(diff.Payload);
        }, cancellationToken);

    /// <summary>Writes a Markdown changelog with authors and dates between two versions.</summary>
    [McpServerTool(Name = "figma_generate_changelog", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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
            var changelog = await comparison.ChangelogAsync(Diff(fileUrl, fromVersion, toVersion, componentIds, mode), ct).ConfigureAwait(false);

            return ToolResponse.Ok(changelog);
        }, cancellationToken);

    /// <summary>Finds by binary search the version in which a property or a child of a node first appeared.</summary>
    [McpServerTool(Name = "figma_blame_node", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Version.BlameNode)]
    public Task<string> BlameNodeAsync(
        [Description("Node id, usually a COMPONENT_SET. Empty takes the first node of the current selection.")] string? nodeId = null,
        [Description("Key from componentPropertyDefinitions, for example 'Disabled#1:2'.")] string? targetComponentProperty = null,
        [Description("Child id — when it first appeared under the node.")] string? targetChildNodeId = null,
        [Description("Link to the Figma file.")] string? fileUrl = null,
        [Description("The version to walk back from. The current one by default.")] string startVersion = VersionSnapshots.Current,
        [Description("How many versions to look back through, from 2 to 500.")] int maxVersionsToWalk = 200,
        [Description("Include autosaves in the search.")] bool includeAutosaves = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var resolvedNodeId = nodeId is null ? SelectedNodeIds().FirstOrDefault() : NodeId.Normalize(nodeId);
            var request = new BlameRequest(
                fileKey,
                resolvedNodeId,
                IsNodeFromSelection: nodeId is null && resolvedNodeId is not null,
                targetComponentProperty,
                targetChildNodeId,
                startVersion,
                maxVersionsToWalk,
                includeAutosaves);

            var answer = await blame.BlameAsync(request, ct).ConfigureAwait(false);

            return answer.Payload is { } payload ? ToolResponse.Ok(payload) : ToolResponse.Error(answer.Error!, answer.Hint);
        }, cancellationToken);

    /// <summary>The comparison the call asks for: the nodes it names, or else the selection, and the plugin's buffer of metadata edits.</summary>
    private DiffRequest Diff(string? fileUrl, string fromVersion, string toVersion, string? componentIds, string mode)
    {
        var fileKey = activeFile.Resolve(fileUrl);
        var named = SplitIds(componentIds);
        var selected = named.Count == 0 ? SelectedNodeIds() : [];
        var session = Plugin.Documents.Resolve();

        return new DiffRequest(fileKey, fromVersion, toVersion, named, selected, mode, session?.MetadataChanges);
    }

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

    /// <summary>Saves a named checkpoint into the version history of the file through the plugin.</summary>
    [McpServerTool(Name = "figma_save_version", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
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
                    {{ScriptLiteral.Of(title)}}{{(description is null ? "" : $", {ScriptLiteral.Of(description)}")}});
                return { versionId: result && result.id ? result.id : null, title: {{ScriptLiteral.Of(title)}} };
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
