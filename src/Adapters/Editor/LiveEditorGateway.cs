using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>High-level bridge wrapper: sends a command, extracts affected nodes from the reply and invalidates their snapshots.</summary>
/// <remarks>One gateway serves every agent: it asks the roster whose call is in hand each time rather than holding an agent, so a service built once still sends each agent's commands to that agent's file.</remarks>
public sealed class LiveEditorGateway(
    EditorCommandRouter router,
    AgentRoster agents,
    SnapshotInvalidator invalidation,
    ILogger<LiveEditorGateway> logger)
    : ILiveEditor
{
    public bool IsConnected => Agent.HasConnection;

    /// <summary>The agent whose call is in hand.</summary>
    public AgentSlot Agent => agents.Current;

    public IOpenDocuments Documents => Agent;

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
            : router.SendAsync(Target(fileKey), Agent, AccessOf(command), command.Name, parameters, timeout, cancellationToken);
    }

    /// <summary>A read runs alongside the agent's other reads; reloading the plugin ends every command in flight, so it runs alone like a write.</summary>
    private static CommandAccess AccessOf(EditorCommand command) =>
        command == EditorCommands.ReloadUi ? CommandAccess.Write : CommandAccess.Read;

    /// <summary>The live session the command goes to: the named file, or the agent's target.</summary>
    private EditorSession Target(string? fileKey)
    {
        var agent = Agent;

        return agent.Resolve(fileKey) ?? throw agent.Unavailable(fileKey);
    }

    /// <summary>Variables from the session snapshot while it is fresh, otherwise read again in the plugin.</summary>
    /// <remarks>GET_VARIABLES_DATA is answered by ui.html from its own cache, which is exactly as old as ours; only REFRESH_VARIABLES makes code.js read the file, and its reply carries the data. A bundle whose reply carries none is asked the old way.</remarks>
    private async Task<JsonNode?> ReadVariablesAsync(string? fileKey, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var session = Target(fileKey);

        if (session.HasFreshVariables)
        {
            return session.VariablesSnapshot!.DeepClone();
        }

        var refreshed = await router
            .SendAsync(session, Agent, CommandAccess.Read, EditorCommands.RefreshVariables.Name, null, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (refreshed?["data"] is not JsonObject variables)
        {
            return await router
                .SendAsync(session, Agent, CommandAccess.Read, EditorCommands.GetVariablesData.Name, null, timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        session.RememberVariables(variables.DeepClone());

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
        var session = Target(fileKey);
        session.DoubtVariables();

        var result = await router.SendAsync(session, Agent, CommandAccess.Write, command.Name, parameters, timeout, cancellationToken).ConfigureAwait(false);

        var affected = ExtractAffectedNodeIds(result, parameters);
        var ancestors = ExtractAncestorIds(result);

        if (affected.Count > 0 || ancestors.Count > 0)
        {
            await invalidation
                .OnNodesMutatedAsync(session.FileKey, affected, ancestors, cancellationToken)
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
            Target(fileKey),
            Agent,
            CommandAccess.Read,
            EditorCommands.ExecuteCode.Name,
            new JsonObject { ["code"] = code, ["timeout"] = 20000 },
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
