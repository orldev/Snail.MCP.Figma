using System.Net;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Where the shared server answers MCP: a loopback address outside the bridge's port range.</summary>
public sealed record SharedMcpEndpoint(IPAddress Address, int Port)
{
    /// <exception cref="ArgumentException">The address is not a loopback one: the server would answer anyone who reaches it.</exception>
    public static SharedMcpEndpoint From(string url)
    {
        var uri = new Uri(url);

        if (!uri.IsLoopback)
        {
            throw new ArgumentException($"'{url}' is not a loopback address: the server would answer anyone who reaches it.", nameof(url));
        }

        var address = IPAddress.TryParse(uri.Host, out var parsed) ? parsed : IPAddress.Loopback;

        return new SharedMcpEndpoint(address, uri.Port);
    }
}
