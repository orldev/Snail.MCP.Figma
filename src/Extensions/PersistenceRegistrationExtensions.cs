using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Persistence.Sqlite;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Driven adapter for snapshot storage: the only place that knows it is SQLite.</summary>
public static class PersistenceRegistrationExtension
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSnapshotPersistence()
        {
            services.AddSingleton<ISnapshotDatabase, SqliteSnapshotDatabase>();
            services.AddSingleton<ISnapshotStoreHealth, SqliteStoreHealth>();
            services.AddSingleton<INodeSnapshotStore, SqliteNodeSnapshotStore>();
            services.AddSingleton<IRenderSnapshotStore, SqliteRenderSnapshotStore>();
            services.AddSingleton<IDocumentVersionLedger, SqliteDocumentVersionLedger>();
            services.AddHostedService<SnapshotCacheWarmUp>();

            return services;
        }
    }
}
