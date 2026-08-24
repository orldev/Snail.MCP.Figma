using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Persistence.LiteDb;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Driven adapter for snapshot storage: the only place that knows it is LiteDB.</summary>
public static class PersistenceRegistrationExtension
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddSnapshotPersistence()
        {
            services.AddSingleton<ILiteDatabaseProvider, LiteDatabaseProvider>();
            services.AddSingleton<ISnapshotStoreHealth, LiteDbStoreHealth>();
            services.AddSingleton<INodeSnapshotStore, LiteDbNodeSnapshotStore>();
            services.AddSingleton<IRenderSnapshotStore, LiteDbRenderSnapshotStore>();
            services.AddSingleton<IDocumentVersionLedger, LiteDbDocumentVersionLedger>();

            return services;
        }
    }
}
