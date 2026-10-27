using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Rest;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Driven adapters to the outside world: the published source (REST) and the live editor (WebSocket).</summary>
public static class IntegrationRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the REST gateway behind its ports and the live editor's bridge, agents and plugin installer.</summary>
        public IServiceCollection AddIntegration() =>
            services
                .AddPublishedSource()
                .AddLiveEditor();

        internal IServiceCollection AddPublishedSource()
        {
            services.AddHttpClient(RestDocumentGateway.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
            });

            services.AddSingleton<QuotaGate>();
            services.AddSingleton<IQuotaStatus>(provider => provider.GetRequiredService<QuotaGate>());

            services.AddSingleton<RestDocumentGateway>();
            services.AddSingleton<IPublishedDocumentSource>(provider => provider.GetRequiredService<RestDocumentGateway>());
            services.AddSingleton<IRenderSource>(provider => provider.GetRequiredService<RestDocumentGateway>());
            services.AddSingleton<IDocumentHistory>(provider => provider.GetRequiredService<RestDocumentGateway>());
            services.AddSingleton<IDocumentCommentary>(provider => provider.GetRequiredService<RestDocumentGateway>());
            services.AddSingleton<ILibraryCatalog>(provider => provider.GetRequiredService<RestDocumentGateway>());
            services.AddSingleton<IApiIdentity>(provider => provider.GetRequiredService<RestDocumentGateway>());

            return services;
        }

        internal IServiceCollection AddLiveEditor()
        {
            services.AddSingleton<OpenDocumentRegistry>();
            services.AddSingleton<AgentRoster>();
            services.AddTransient(provider => provider.GetRequiredService<AgentRoster>().Current);
            services.AddTransient<IOpenDocuments>(provider => provider.GetRequiredService<AgentSlot>());
            services.AddTransient(provider => provider.GetRequiredService<AgentSlot>().Tracker);
            services.AddSingleton(provider => BridgeToken.LoadOrCreate(provider.GetRequiredService<ServerConfig>().DataDirectory));
            services.AddSingleton<EditorCommandRouter>();
            services.AddSingleton<EditorSocketHub>();
            services.AddSingleton<ILiveEditorLink>(provider => provider.GetRequiredService<EditorSocketHub>());
            services.AddSingleton<LiveEditorGateway>();
            services.AddSingleton<ILiveEditor>(provider => provider.GetRequiredService<LiveEditorGateway>());
            services.AddSingleton<CaptureStore>();
            services.AddSingleton<ICaptureStore>(provider => provider.GetRequiredService<CaptureStore>());
            services.AddSingleton<ImageFiles>();
            services.AddSingleton<IImageFiles>(provider => provider.GetRequiredService<ImageFiles>());
            services.AddSingleton<EditorExtensionInstaller>();
            services.AddSingleton<IEditorExtensionInstaller>(provider => provider.GetRequiredService<EditorExtensionInstaller>());
            services.AddHostedService(provider => provider.GetRequiredService<EditorExtensionInstaller>());

            services.AddSiblingProbe();
            services.AddHostedService<SiblingDiscovery>();

            return services;
        }

        /// <summary>The client that knocks on the other bridge ports: a second to answer, and nothing written about the knock.</summary>
        /// <remarks>Most ports of the range have no server, so the probe is refused nine times every 45 seconds by design; the SDK's logging handlers wrote each refusal with its stack at Information, 5.2 million stack lines in 11 days on the shared server.</remarks>
        private void AddSiblingProbe()
        {
            services.AddHttpClient(SiblingDiscovery.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(1);
            }).RemoveAllLoggers();
        }
    }
}
