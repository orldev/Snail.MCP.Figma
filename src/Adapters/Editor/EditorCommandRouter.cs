using System.Collections.Concurrent;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Matches plugin replies to outstanding requests by <c>requestId</c>; replies arrive out of order, so each wait has its own timeout.</summary>
public sealed class EditorCommandRouter(
    EditorLinkOptions options,
    ILogger<EditorCommandRouter> logger)
{
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);

    private long _counter;

    /// <summary>Requests currently awaiting a reply.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Sends an agent's command to the file's plugin when its turn comes and awaits the reply; throws <see cref="EditorUnavailableException"/> when the send fails or the agent left while waiting, and <see cref="TimeoutException"/> on silence.</summary>
    /// <remarks>Which file is the caller's decision: the router serves every agent and knows none of their targets. The timeout starts when the command leaves the agent's queue: waiting for its own previous command is not the plugin's silence.</remarks>
    public async Task<JsonNode?> SendAsync(
        EditorSession session,
        AgentSlot? agent,
        string method,
        JsonObject? parameters = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (agent is null)
        {
            return await DispatchAsync(session, null, method, parameters, timeout, cancellationToken).ConfigureAwait(false);
        }

        var lane = agent.LaneTo(session);

        using (var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, agent.Departure))
        {
            try
            {
                await lane.WaitAsync(turn.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new EditorUnavailableException($"{method} was not sent: the agent's session ended while the command waited for its turn.");
            }
        }

        try
        {
            return await DispatchAsync(session, agent.Address, method, parameters, timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lane.Release();
        }
    }

    private async Task<JsonNode?> DispatchAsync(
        EditorSession session,
        string? agent,
        string method,
        JsonObject? parameters,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var id = $"snail_{Interlocked.Increment(ref _counter)}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(options.RequestTimeoutSeconds);

        var pending = new PendingRequest(method, effectiveTimeout, session.ConnectionId);
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
            await session.SendAsync(EditorWireProtocol.Request(id, method, parameters, agent), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);

            throw new EditorUnavailableException($"Could not send {method} to the plugin: {ex.Message}", ex);
        }

        session.LastActivityAt = DateTimeOffset.UtcNow;
        Log.CommandSent(logger, method, id, session.FileKey);

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

    /// <summary>Fails the waits of a connection that dropped at once, instead of letting them run into their timeouts.</summary>
    public void FailFor(EditorSession session, string reason)
    {
        foreach (var (id, pending) in _pending.ToArray())
        {
            if (pending.ConnectionId == session.ConnectionId && _pending.TryRemove(id, out var failed))
            {
                failed.Completion.TrySetException(new EditorUnavailableException(reason));
            }
        }
    }

    private sealed record PendingRequest(string Method, TimeSpan Timeout, string ConnectionId)
    {
        public TaskCompletionSource<JsonNode?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
