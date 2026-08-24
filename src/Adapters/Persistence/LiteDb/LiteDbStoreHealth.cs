using Snail.MCP.Figma.Application.Snapshots;

namespace Snail.MCP.Figma.Adapters.Persistence.LiteDb;

/// <summary><see cref="ISnapshotStoreHealth"/> over LiteDB.</summary>
public sealed class LiteDbStoreHealth(
    ILiteDatabaseProvider provider,
    ILogger<LiteDbStoreHealth> logger)
    : ISnapshotStoreHealth
{
    public bool IsStarted => provider.IsStarted;

    public string? LastError => provider.LastError;

    public SnapshotStoreProbe Probe()
    {
        try
        {
            var database = provider.GetDatabase();

            return new SnapshotStoreProbe(
                Ok: true,
                NodeEntries: database.GetCollection<NodeSnapshot>(LiteDbCollections.Nodes).Count(),
                ImageEntries: database.GetCollection<RenderSnapshot>(LiteDbCollections.Images).Count(),
                Error: null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LiteDB cache failed to open");

            return SnapshotStoreProbe.Failed(ex.Message);
        }
    }
}
