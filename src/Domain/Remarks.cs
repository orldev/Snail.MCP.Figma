namespace Snail.MCP.Figma.Domain;

/// <summary>What the human typed in the plugin panel and nobody has read yet, oldest first.</summary>
/// <remarks>Reading takes: a remark is addressed to one reader, and a copy left behind would stop the next build over words already dealt with. The cap keeps a panel talking to an absent agent from growing without bound.</remarks>
public sealed class Remarks(int capacity)
{
    private readonly List<BuildInterruption> _pending = [];

    private readonly object _gate = new();

    private TaskCompletionSource _arrival = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Capacity { get; } = capacity;

    public int Count
    {
        get { lock (_gate) { return _pending.Count; } }
    }

    /// <summary>Remembers a remark, dropping the oldest once the cap is reached.</summary>
    public void Add(BuildInterruption remark)
    {
        lock (_gate)
        {
            if (_pending.Count >= Capacity)
            {
                _pending.RemoveAt(0);
            }

            _pending.Add(remark);
            var waiting = _arrival;
            _arrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.TrySetResult();
        }
    }

    /// <summary>Waits for a remark to arrive, or gives up after <paramref name="timeout"/>.</summary>
    /// <remarks>MCP cannot wake an agent, so an agent that wants to hear the human within a second has to hold a call open here; anything already waiting returns at once.</remarks>
    public async Task<bool> TryWaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task arrival;

        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                return true;
            }

            arrival = _arrival.Task;
        }

        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var finished = await Task.WhenAny(arrival, Task.Delay(timeout, expiry.Token)).ConfigureAwait(false);

        await expiry.CancelAsync().ConfigureAwait(false);

        return finished == arrival;
    }

    /// <summary>Takes everything waiting, oldest first.</summary>
    public IReadOnlyList<BuildInterruption> TakeAll()
    {
        lock (_gate)
        {
            var taken = _pending.ToArray();
            _pending.Clear();

            return taken;
        }
    }

    /// <summary>Takes the oldest remark left after <paramref name="moment"/>, or <c>null</c> when none was.</summary>
    /// <remarks>A build answers only for words typed while it was running; anything older belongs to the previous result and waits to be read instead of killing the run that just started.</remarks>
    public BuildInterruption? TakeSince(DateTimeOffset moment)
    {
        lock (_gate)
        {
            var index = _pending.FindIndex(remark => remark.At >= moment);

            if (index < 0)
            {
                return null;
            }

            var found = _pending[index];
            _pending.RemoveAt(index);

            return found;
        }
    }
}
