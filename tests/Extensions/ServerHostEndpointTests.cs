using Microsoft.Extensions.Configuration;
using Snail.MCP.Figma.Extensions;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Figma.Tests.Extensions;

/// <summary>The server has no key of its own, so its MCP port stays on loopback whatever the settings ask for.</summary>
public class ServerHostEndpointTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();

    [Fact]
    public void ReadEndpoint_NothingSet_IsStdio()
    {
        var endpoint = ServerHost.ReadEndpoint(Settings());

        Assert.Equal(McpTransport.Stdio, endpoint.Transport);
    }

    [Fact]
    public void ReadEndpoint_HttpOnLoopback_ListensThere()
    {
        var endpoint = ServerHost.ReadEndpoint(Settings(("Transport", "Http"), ("Url", "http://127.0.0.1:9240")));

        Assert.Equal(McpTransport.Http, endpoint.Transport);
        Assert.Equal(9240, endpoint.Url.Port);
    }

    [Fact]
    public void ReadEndpoint_EveryInterface_IsRefusedNamingTheVariable()
    {
        var refusal = Assert.Throws<StartRefusedException>(
            () => ServerHost.ReadEndpoint(Settings(("Transport", "Http"), ("Url", "http://0.0.0.0:9233"))));

        Assert.Contains("SNAIL_MCP_FIGMA_URL", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadEndpoint_AddressBeyondLoopback_IsRefused() =>
        Assert.Throws<StartRefusedException>(
            () => ServerHost.ReadEndpoint(Settings(("Transport", "Http"), ("Url", "http://192.168.1.20:9233"))));

    [Fact]
    public void ReadEndpoint_ListensOnEveryInterface_IsRefused() =>
        Assert.Throws<StartRefusedException>(
            () => ServerHost.ReadEndpoint(Settings(("Transport", "Http"), ("ListensOnEveryInterface", "true"))));

    [Fact]
    public void ReadEndpoint_AddressInTheSnailSection_IsReadThere()
    {
        var endpoint = ServerHost.ReadEndpoint(Settings(("Snail:Transport", "Http"), ("Snail:Url", "http://127.0.0.1:9241")));

        Assert.Equal(9241, endpoint.Url.Port);
    }
}
