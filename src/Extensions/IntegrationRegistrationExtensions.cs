using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Rest;

namespace Snail.MCP.Figma.Extensions;

/// <summary>Driven adapters to the outside world: the published source (REST) and the live editor (WebSocket).</summary>
public static class IntegrationRegistrationExtensions
{
    extension(IServiceCollection services)
    {
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
            services.AddSingleton<IOpenDocuments>(provider => provider.GetRequiredService<OpenDocumentRegistry>());
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

            services.AddHttpClient(SiblingDiscovery.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(1);
            });

            services.AddHostedService<SiblingDiscovery>();

            return services;
        }
    }
}
