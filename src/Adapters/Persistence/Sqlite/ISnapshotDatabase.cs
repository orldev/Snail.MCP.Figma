using Microsoft.Data.Sqlite;

namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>The snapshot cache file, prepared on first use: the MCP handshake must not wait for storage.</summary>
public interface ISnapshotDatabase
{
    /// <summary>The file is open and its tables exist.</summary>
    bool IsStarted { get; }

    /// <summary>Why the last open failed, if it did.</summary>
    string? LastError { get; }

    /// <summary>An open connection to the cache, preparing it first if needed; throws when the file cannot be opened.</summary>
    SqliteConnection Open();

    /// <summary>Same as <see cref="Open"/>, but <c>null</c> when the cache is disabled or failed a moment ago.</summary>
    SqliteConnection? TryOpen();

    /// <summary><see cref="TryOpen"/> for a caller that awaits.</summary>
    Task<SqliteConnection?> TryOpenAsync(CancellationToken cancellationToken);
}
