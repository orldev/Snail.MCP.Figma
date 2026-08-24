using Snail.MCP.Figma.Adapters.Persistence.LiteDb;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>One cache file per run in a temp directory: cache tests write to a real LiteDB, not a mock.</summary>
public sealed class LiteDbFixture : IAsyncLifetime
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "snail-figma-tests", Guid.NewGuid().ToString("N"));

    private LiteDatabaseProvider? _provider;

    public ILiteDatabaseProvider Database => _provider!;

    public Task InitializeAsync()
    {
        var config = new ServerConfig
        {
            DataDirectory = _dataDirectory,
            Cache = { TtlHours = 1 },
        };

        _provider = new LiteDatabaseProvider(config, NullLogger<LiteDatabaseProvider>.Instance);
        _provider.GetDatabase();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _provider?.Dispose();

        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }
}

[CollectionDefinition(nameof(LiteDbFixture))]
public sealed class LiteDbCollection : ICollectionFixture<LiteDbFixture>;
