using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Logging;
using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Adapters.Rest;
using Snail.MCP.Figma.Extensions;

namespace Snail.MCP.Figma.Tests.Extensions;

/// <summary>What the integration registration hangs on each HttpClient.</summary>
public class IntegrationRegistrationTests
{
    /// <summary>The probe of the sibling ports is refused on most of them every 45 seconds by design; the SDK's logging handlers wrote each refusal with its stack, 527 MB of log in 11 days on the shared server.</summary>
    [Fact]
    public void AddIntegration_SiblingDiscoveryClient_CarriesNoLoggingHandler()
    {
        var chain = HandlerChain(SiblingDiscovery.HttpClientName);

        Assert.DoesNotContain(chain, handler => handler is LoggingHttpMessageHandler or LoggingScopeHttpMessageHandler);
    }

    /// <summary>The REST client keeps its loggers: a call to Figma is rare and worth a line.</summary>
    [Fact]
    public void AddIntegration_FigmaClient_KeepsItsLoggingHandlers()
    {
        var chain = HandlerChain(RestDocumentGateway.HttpClientName);

        Assert.Contains(chain, handler => handler is LoggingHttpMessageHandler or LoggingScopeHttpMessageHandler);
    }

    private static List<HttpMessageHandler> HandlerChain(string clientName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegration();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var chain = new List<HttpMessageHandler>();
        HttpMessageHandler? link = factory.CreateHandler(clientName);

        while (link is not null)
        {
            chain.Add(link);
            link = (link as DelegatingHandler)?.InnerHandler;
        }

        return chain;
    }
}
