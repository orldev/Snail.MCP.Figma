using Snail.MCP.Figma.Adapters.Persistence.LiteDb;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Provider that always reports "no database" — bridge tests need no cache.</summary>
public sealed class DisabledLiteDatabaseProvider : ILiteDatabaseProvider
{
    public bool IsStarted => false;

    public string? LastError => "cache disabled in test";

    public LiteDB.LiteDatabase GetDatabase() => throw new InvalidOperationException("cache disabled in test");

    public LiteDB.LiteDatabase? TryGetDatabase() => null;
}
