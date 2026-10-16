namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Cache self-check that hides the database from the tools layer.</summary>
public interface ISnapshotStoreHealth
{
    /// <summary>Whether the cache database is open and its tables exist.</summary>
    bool IsStarted { get; }

    /// <summary>Why the last attempt to open the cache failed; <c>null</c> when it has not failed.</summary>
    string? LastError { get; }

    /// <summary>Opens the database and counts entries; expensive, so only on explicit request.</summary>
    SnapshotStoreProbe Probe();
}

/// <summary>Cache probe result: counters or the open error.</summary>
public sealed record SnapshotStoreProbe(bool Succeeded, int NodeEntries, int ImageEntries, string? Error)
{
    /// <summary>Probe result for a cache that could not be opened, with zero counters.</summary>
    public static SnapshotStoreProbe Failed(string error) => new(false, 0, 0, error);
}
