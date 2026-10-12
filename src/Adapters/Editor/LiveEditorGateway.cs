using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>High-level bridge wrapper: sends a command, extracts affected nodes from the reply and invalidates their snapshots.</summary>
public sealed class LiveEditorGateway(
    EditorCommandRouter router,
    OpenDocumentRegistry registry,
    SnapshotInvalidator invalidation,
    ILogger<LiveEditorGateway> logger)
    : ILiveEditor
{
    public bool IsConnected => registry.HasConnection;

    public OpenDocumentRegistry Registry => registry;

    public IOpenDocuments Documents => registry;

    /// <summary>Side-effect-free command — a read through the plugin.</summary>
    public Task<JsonNode?> ReadAsync(
        EditorCommand command,
        JsonObject? parameters = null,
        string? fileKey = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return command == EditorCommands.GetVariablesData
            ? ReadVariablesAsync(fileKey, timeout, cancellationToken)
            : router.SendAsync(command.Name, parameters, fileKey, timeout, cancellationToken);
    }

    /// <summary>Variables from the session snapshot while it is fresh, otherwise read again in the plugin.</summary>
    /// <remarks>GET_VARIABLES_DATA is answered by ui.html from its own cache, which is exactly as old as ours; only REFRESH_VARIABLES makes code.js read the file, and its reply carries the data. A bundle whose reply carries none is asked the old way.</remarks>
    private async Task<JsonNode?> ReadVariablesAsync(string? fileKey, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var session = registry.Resolve(fileKey);

        if (session is { HasFreshVariables: true })
        {
            return session.VariablesSnapshot!.DeepClone();
        }

        var refreshed = await router
            .SendAsync(EditorCommands.RefreshVariables.Name, null, fileKey, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed?["data"] is not JsonObject variables)
        {
            return await router
                .SendAsync(EditorCommands.GetVariablesData.Name, null, fileKey, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        session?.RememberVariables(variables.DeepClone());

        return variables.DeepClone();
    }

    /// <summary>Mutation; only the plugin reply confirms the write, since REST lags behind saved state.</summary>
    public async Task<JsonNode?> ApplyAsync(
        EditorCommand command,
        JsonObject parameters,
        string? fileKey = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var session = registry.Resolve(fileKey);
        session?.DoubtVariables();

        var result = await router.SendAsync(command.Name, parameters, fileKey, timeout, cancellationToken).ConfigureAwait(false);

        var affected = ExtractAffectedNodeIds(result, parameters);
        var ancestors = ExtractAncestorIds(result);

        if (affected.Count > 0 || ancestors.Count > 0)
        {
            await invalidation
                .OnNodesMutatedAsync(session?.FileKey, affected, ancestors, cancellationToken)
                .ConfigureAwait(false);

            Log.NodesInvalidated(logger, command.Name, affected.Count, ancestors.Count);
        }

        return result;
    }

    public async Task<JsonNode?> ReadTreeAsync(string fileKey, string? nodeId, int depth, int maxNodes, CancellationToken cancellationToken = default) =>
        await ExecuteScriptAsync(
            fileKey,
            nodeId is null ? EditorScripts.FileTree(depth, maxNodes) : EditorScripts.Subtree(nodeId, depth, maxNodes),
            cancellationToken).ConfigureAwait(false);

    public Task<JsonNode?> ReadStylesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        ExecuteScriptAsync(fileKey, EditorScripts.Styles, cancellationToken);

    /// <summary>Runs read-only JavaScript in the sandbox of this file and unwraps the EXECUTE_CODE envelope; snapshots are never touched.</summary>
    private async Task<JsonNode?> ExecuteScriptAsync(string fileKey, string code, CancellationToken cancellationToken)
    {
        var response = await router.SendAsync(
            EditorCommands.ExecuteCode.Name,
            new JsonObject { ["code"] = code, ["timeout"] = 20000 },
            fileKey,
            TimeSpan.FromSeconds(25),
            cancellationToken).ConfigureAwait(false);

        if (response?["success"]?.GetValue<bool>() == false)
        {
            throw new EditorCommandException(
                $"The plugin failed to read: {response["error"]?.GetValue<string>() ?? "no reason given"}");
        }

        return response?["result"];
    }

    /// <summary>Drops every snapshot of the file — for edits of unknown scope, such as figma_execute.</summary>
    public Task InvalidateWholeFileAsync(string fileKey, CancellationToken cancellationToken = default) =>
        invalidation.OnDocumentChangedAsync(fileKey, [], hasStyleChanges: true, cancellationToken);

    /// <summary>Affected nodes: what the plugin reported, otherwise what was sent, since not every command echoes nodeId.</summary>
    internal static IReadOnlyCollection<string> ExtractAffectedNodeIds(JsonNode? result, JsonObject parameters)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        CollectIds(result?["nodeId"], ids);
        CollectIds(result?["nodeIds"], ids);
        CollectIds(result?["affectedNodeIds"], ids);
        CollectIds(result?["id"], ids);

        if (ids.Count == 0)
        {
            CollectIds(parameters["nodeId"], ids);
            CollectIds(parameters["nodeIds"], ids);
            CollectIds(parameters["parentId"], ids);
        }

        return ids;
    }

    /// <summary>Ancestors of the affected node: Auto Layout reflows upwards, so the parent is stale too.</summary>
    internal static IReadOnlyCollection<string> ExtractAncestorIds(JsonNode? result)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(result?["parentId"], ids);
        CollectIds(result?["ancestorIds"], ids);
        CollectIds(result?["parent"]?["id"], ids);

        return ids;
    }

    private static void CollectIds(JsonNode? node, HashSet<string> target)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    CollectIds(item, target);
                }

                break;

            case JsonValue value when value.TryGetValue<string>(out var id) && !string.IsNullOrWhiteSpace(id):
                target.Add(NodeId.Normalize(id));
                break;
        }
    }
}
