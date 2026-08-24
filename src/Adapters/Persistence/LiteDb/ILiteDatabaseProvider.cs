using LiteDB;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary>Lazily opened cache database: the MCP stdio handshake must not wait for storage.</summary>
public interface ILiteDatabaseProvider
{
    /// <summary>The database is already open.</summary>
    bool IsStarted { get; }

    /// <summary>Last open error, if opening failed.</summary>
    string? LastError { get; }

    /// <summary>Opens the database if needed and returns it; throws on failure.</summary>
    LiteDatabase GetDatabase();

    /// <summary>Same as <see cref="GetDatabase"/> but returns <c>null</c> when the cache is unavailable or disabled.</summary>
    LiteDatabase? TryGetDatabase();
}
