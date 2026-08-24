using System.Text.Json;
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
    protected async Task<string> ExecuteAsync(Func<CancellationToken, Task<string>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (EditorUnavailableException ex)
        {
            Logger.LogWarning("Bridge unavailable: {Message}", ex.Message);

            return NotConnected();
        }
        catch (EditorCommandException ex)
        {
            return ToolResponse.Error(ex.Message, Messages.CheckNodeIdHint);
        }
        catch (TimeoutException ex)
        {
            return ToolResponse.Error(ex.Message, Messages.PluginWindowHint);
        }
        catch (PublishedSourceException ex)
        {
            return ToolResponse.Error(ex.Message, ex.Hint);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ToolResponse.Error(ex.Message);
        }
    }
}
