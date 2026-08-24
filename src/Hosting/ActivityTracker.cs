namespace Snail.MCP.Figma.Hosting;

/// <summary>Thread-safe timestamp of the last tool invocation.</summary>
public sealed class ActivityTracker(TimeProvider timeProvider) : IActivityTracker
{
    private long _lastActivityTicks = timeProvider.GetUtcNow().UtcTicks;

    public DateTime LastActivityUtc => new(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);

    public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, timeProvider.GetUtcNow().UtcTicks);
}
