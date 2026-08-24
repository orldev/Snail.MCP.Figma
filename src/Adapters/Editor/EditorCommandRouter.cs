using System.Collections.Concurrent;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Matches plugin replies to outstanding requests by <c>requestId</c>; replies arrive out of order, so each wait has its own timeout.</summary>
public sealed class EditorCommandRouter(
    OpenDocumentRegistry registry,
    EditorLinkOptions options,
    ILogger<EditorCommandRouter> logger)
{
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);

    private long _counter;

    /// <summary>Requests currently awaiting a reply.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Sends a command and awaits the reply; throws <see cref="EditorUnavailableException"/> without a plugin and <see cref="TimeoutException"/> on silence.</summary>
    public async Task<JsonNode?> SendAsync(
        string method,
        JsonObject? parameters = null,
        string? fileKey = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var session = registry.Resolve(fileKey)
                      ?? throw new EditorUnavailableException(
                          "The Snail Bridge plugin is not connected. Open the file in Figma Desktop and run the plugin " +
                          "(Plugins → Development → Snail Bridge).");

        var id = $"snail_{Interlocked.Increment(ref _counter)}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

        var pending = new PendingRequest(method, effectiveTimeout);
        _pending[id] = pending;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);

        await using var registration = timeoutSource.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var abandoned))
            {
                abandoned.Completion.TrySetException(cancellationToken.IsCancellationRequested
                    ? new OperationCanceledException(cancellationToken)
                    : new TimeoutException(
                        $"The plugin did not answer {method} within {effectiveTimeout.TotalSeconds:0} s. " +
                        "Check that the plugin window is open in Figma."));
            }
        }).ConfigureAwait(false);

        try
        {
            await session.SendAsync(EditorWireProtocol.Request(id, method, parameters), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);

            throw new EditorUnavailableException($"Could not send {method} to the plugin: {ex.Message}", ex);
        }

        session.LastActivityAt = DateTimeOffset.UtcNow;
        logger.LogDebug("→ plugin {Method} (id={Id}, file={FileKey})", method, id, session.FileKey);

        return await pending.Completion.Task.ConfigureAwait(false);
    }

    /// <summary>Completes a pending wait with a plugin reply; <c>false</c> means the message is an event, not a reply.</summary>
    public bool TryComplete(JsonNode message)
    {
        var id = message["id"]?.GetValue<string>();

        if (id is null || !_pending.TryRemove(id, out var pending))
        {
            return false;
        }

        var error = message["error"];

        if (error is not null)
        {
            pending.Completion.TrySetException(new EditorCommandException(
                $"The plugin rejected {pending.Method}: {error.ToJsonString().Trim('"')}"));

            return true;
        }

        pending.Completion.TrySetResult(message["result"]?.DeepClone());

        return true;
    }

    /// <summary>Fails every pending wait when the connection drops, instead of letting them run into their timeouts.</summary>
    public void FailAll(string reason)
    {
        foreach (var id in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.Completion.TrySetException(new EditorUnavailableException(reason));
            }
        }
    }

    private sealed record PendingRequest(string Method, TimeSpan Timeout)
    {
        public TaskCompletionSource<JsonNode?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
