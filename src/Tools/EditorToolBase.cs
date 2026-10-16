using System.Text.Json;
using ModelContextProtocol.Protocol;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Shared base of plugin-backed tools: sends commands and turns bridge errors into readable responses.</summary>
public abstract class EditorToolBase(ILiveEditor plugin, ILogger logger)
{
    protected ILiveEditor Plugin { get; } = plugin;

    protected ILogger Logger { get; } = logger;

    /// <summary>Mutation with cache invalidation of the affected nodes.</summary>
    protected Task<string> ApplyAsync(
        EditorCommand command,
        JsonObject parameters,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Plugin.ApplyAsync(command, parameters, timeout: timeout, cancellationToken: ct)
                .ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    /// <summary>Read through the plugin; the cache is untouched.</summary>
    protected Task<string> ReadAsync(
        EditorCommand command,
        JsonObject? parameters,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null) =>
        ExecuteAsync(async ct =>
        {
            var result = await Plugin.ReadAsync(command, parameters, timeout: timeout, cancellationToken: ct)
                .ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject());
        }, cancellationToken);

    /// <summary>Parses a JSON parameter passed as a string; the error text hints at the expected shape.</summary>
    protected static bool TryParseJson(string? json, out JsonNode? node, out string? error)
    {
        node = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            node = JsonNode.Parse(json);

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;

            return false;
        }
    }

    protected static string NotConnected() =>
        ToolResponse.Error(Messages.PluginNotConnected, Messages.HowToConnectPlugin);

    /// <summary>Every connected file, marked with whether it is the target and who else works in it.</summary>
    protected JsonArray DescribeFiles() => new([.. Plugin.Documents.Sessions.Select(session =>
    {
        var described = session.Describe();
        described["target"] = session.FileKey == Plugin.Documents.ActiveFileKey;
        described["othersHere"] = new JsonArray([.. Plugin.Documents.OthersIn(session).Select(agent => (JsonNode)agent)]);

        return (JsonNode)described;
    })]);

    /// <summary>Names of the other agents working in the target file; empty without a target.</summary>
    protected IReadOnlyList<string> OthersInTarget() =>
        Plugin.Documents.ActiveFileKey is { } target && Plugin.Documents.Resolve(target) is { } session ? Plugin.Documents.OthersIn(session) : [];

    /// <summary>Peels the EXECUTE_CODE envelope; the clone detaches the payload so callers can embed it into their own response, and a bridge failure comes back as an <c>__error</c> field.</summary>
    protected static JsonNode? Unwrap(JsonNode? raw)
    {
        if (raw?["success"]?.GetValue<bool>() == false)
        {
            return new JsonObject
            {
                ["__error"] = raw["error"]?.GetValue<string>() ?? "The bridge reported an error with no text.",
            };
        }

        return (raw?["result"] ?? raw)?.DeepClone();
    }

    /// <summary>Bridge errors come back as data so the model can read the cause and fix the call.</summary>
    protected Task<string> ExecuteAsync(Func<CancellationToken, Task<string>> action, CancellationToken cancellationToken) =>
        GuardAsync(action, static (message, hint) => ToolResponse.Error(message, hint), cancellationToken);

    /// <summary>The same guard for a tool that may answer with a picture.</summary>
    /// <remarks>The failure shape is identical, only wrapped as content.</remarks>
    protected Task<CallToolResult> ExecutePicturedAsync(
        Func<CancellationToken, Task<CallToolResult>> action,
        CancellationToken cancellationToken) =>
        GuardAsync(action, static (message, hint) => ToolResponse.Textual(ToolResponse.Error(message, hint)), cancellationToken);

    /// <summary>The same guard for a tool that advertises an output schema: success goes out as structured content, failure as the text envelope alone.</summary>
    protected Task<CallToolResult> ExecuteStructuredAsync(
        Func<CancellationToken, Task<JsonNode>> action,
        CancellationToken cancellationToken) =>
        GuardAsync(
            async ct => ToolResponse.Structured(await action(ct).ConfigureAwait(false)),
            static (message, hint) => ToolResponse.Textual(ToolResponse.Error(message, hint)),
            cancellationToken);

    /// <summary>The hints this family answers failures with.</summary>
    protected virtual FailureHints Hints => FailureHints.Editor;

    /// <summary>The one error ladder of every tool: what differs between result shapes is how the message and the hint are packed, and between families only the hints.</summary>
    /// <remarks>The message is always the failure's own. A command that found no plugin names the file the agent works in and how to reach it; a generic "not connected" in its place sent agents to the wrong file.</remarks>
    private async Task<T> GuardAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<string, string?, T> fail,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (EditorUnavailableException ex)
        {
            Log.BridgeUnavailable(Logger, ex.Message);

            return fail(ex.Message, Hints.NotConnected);
        }
        catch (EditorCommandException ex)
        {
            return fail(ex.Message, Hints.CommandFailed);
        }
        catch (EditorTargetAmbiguousException ex)
        {
            return fail(ex.Message, Messages.HowToChooseTarget);
        }
        catch (TimeoutException ex)
        {
            return fail(ex.Message, Hints.Timeout);
        }
        catch (PublishedSourceException ex)
        {
            Log.ApiError(Logger, ex);

            return fail(ex.Message, ex.Hint);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return fail(ex.Message, Hints.BadArgument);
        }
    }
}
