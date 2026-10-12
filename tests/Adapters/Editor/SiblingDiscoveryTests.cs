using Snail.MCP.Figma.Adapters.Editor;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Tests.Adapters.Editor;

/// <summary>Server-side sibling discovery: which ports get probed and which /health payloads count as our bridge.</summary>
public class SiblingDiscoveryTests
{
    [Fact]
    public void SiblingPortsCoverTheRangeWithoutTheOwnPort()
    {
        var options = new EditorLinkOptions { PortRangeStart = 9223, PortRangeEnd = 9226 };

        var ports = SiblingDiscovery.SiblingPorts(options, ownPort: 9224).ToArray();

        Assert.Equal([9223, 9225, 9226], ports);
    }

    [Fact]
    public void TheBridgeHealthPayloadIsRecognized()
    {
        var payload = JsonNode.Parse("""{"status":"ok","version":"1.2.3","port":9223,"connectedFiles":0,"activeFile":null}""");

        Assert.True(SiblingDiscovery.IsBridgeHealth(payload));
    }

    [Fact]
    public void AForeignServersHealthIsNotMistakenForTheBridge()
    {
        var payload = JsonNode.Parse("""{"status":"ok"}""");

        Assert.False(SiblingDiscovery.IsBridgeHealth(payload));
    }

    [Fact]
    public void PortsAliveCarriesTheTypeAndThePortList()
    {
        var message = EditorWireProtocol.PortsAlive([9223, 9225]);

        Assert.Equal("PORTS_ALIVE", message["type"]!.GetValue<string>());
        Assert.Equal([9223, 9225], [.. message["data"]!["ports"]!.AsArray().Select(port => port!.GetValue<int>())]);
    }
}
