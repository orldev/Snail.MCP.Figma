namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Whether a command only looks at the file or may change it.</summary>
public enum CommandAccess
{
    /// <summary>Reads the file or moves the human's view; runs alongside the agent's other reads.</summary>
    Read,

    /// <summary>Changes the file, or the plugin itself; runs alone among the agent's commands.</summary>
    Write,
}

/// <summary>The turn of one agent's commands in one file: up to <see cref="ReadsAtOnce"/> reads together, a write alone.</summary>
/// <remarks>
/// <para>The plugin runs on one thread and starts every command it is given, awaiting each one's Figma calls in between, so a few reads of one agent overlap well; letting an agent through without limit would let one agent's burst push another agent's short read behind a dozen of its own.</para>
/// <para>A write waits for the agent's reads in flight and holds back the reads that come after it: a read sent after a write must see it. The order is fair — whoever came first goes first — because every command passes the write gate on its way in.</para>
/// </remarks>
public sealed class CommandLane
{
    /// <summary>How many reads of one agent may be in flight in one file.</summary>
    public const int ReadsAtOnce = 3;

    private readonly SemaphoreSlim _reads = new(ReadsAtOnce, ReadsAtOnce);

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Waits for the command's turn; disposing the answer ends the turn.</summary>
    public async Task<IDisposable> EnterAsync(CommandAccess access, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (access is CommandAccess.Read)
        {
            try
            {
                await _reads.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            return new Turn(() => _reads.Release());
        }

        var taken = 0;

        try
        {
            for (; taken < ReadsAtOnce; taken++)
            {
                await _reads.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            if (taken > 0)
            {
                _reads.Release(taken);
            }

            _gate.Release();

            throw;
        }

        return new Turn(() =>
        {
            _reads.Release(ReadsAtOnce);
            _gate.Release();
        });
    }

    private sealed class Turn(Action end) : IDisposable
    {
        private Action? _end = end;

        public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
    }
}
