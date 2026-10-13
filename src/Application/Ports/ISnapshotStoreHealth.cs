namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Cache self-check that hides the database from the tools layer.</summary>
public interface ISnapshotStoreHealth
{
    bool IsStarted { get; }

    string? LastError { get; }

    /// <summary>Opens the database and counts entries; expensive, so only on explicit request.</summary>
    SnapshotStoreProbe Probe();
}

/// <summary>Cache probe result: counters or the open error.</summary>
public sealed record SnapshotStoreProbe(bool Succeeded, int NodeEntries, int ImageEntries, string? Error)
{
    public static SnapshotStoreProbe Failed(string error) => new(false, 0, 0, error);
}
