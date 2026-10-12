using System.Net;
using System.Net.Sockets;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Picks a port for the WebSocket hub within the range hardwired into the plugin's <c>networkAccess</c>.</summary>
public static class PortDiscovery
{
    /// <summary>First free port of the range; <c>null</c> when all are taken and the bridge cannot start.</summary>
    public static int? FindAvailablePort(EditorLinkOptions options, ILogger? logger = null)
    {
        for (var port = options.PortRangeStart; port <= options.PortRangeEnd; port++)
        {
            if (IsAvailable(port))
            {
                if (logger is not null)
                {
                    Log.PortChosen(logger, port);
                }

                return port;
            }

            if (logger is not null)
            {
                Log.PortBusy(logger, port);
            }
        }

        if (logger is not null)
        {
            Log.PortsExhausted(logger, options.PortRangeStart, options.PortRangeEnd);
        }

        return null;
    }

    /// <summary>A port is free only when both loopback addresses are free: Kestrel binds 127.0.0.1 and [::1], and macOS resolves localhost to IPv6 first.</summary>
    internal static bool IsAvailable(int port) =>
        CanBind(IPAddress.Loopback, port) && CanBind(IPAddress.IPv6Loopback, port);

    private static bool CanBind(IPAddress address, int port)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
        {
            return true;
        }

        try
        {
            using var listener = new TcpListener(address, port);
            listener.Start();
            listener.Stop();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
