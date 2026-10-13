using Microsoft.Data.Sqlite;
using Snail.MCP.Figma.Adapters.Persistence.Sqlite;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>A cache that always reports "no database" — bridge tests need no cache.</summary>
public sealed class DisabledSnapshotDatabase : ISnapshotDatabase
{
    public bool IsStarted => false;

    public string? LastError => "cache disabled in test";

    public SqliteConnection Open() => throw new InvalidOperationException("cache disabled in test");

    public SqliteConnection? TryOpen() => null;

    public Task<SqliteConnection?> TryOpenAsync(CancellationToken cancellationToken) => Task.FromResult<SqliteConnection?>(null);
}
