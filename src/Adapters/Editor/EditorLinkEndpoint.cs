namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Address of the WebSocket bridge; the port is picked once before host build and <see cref="Port"/> is empty when the whole range is busy.</summary>
public sealed record EditorLinkEndpoint(int? Port, string Path)
{
    /// <summary>Whether a free port was found, so the bridge accepts plugin connections.</summary>
    public bool IsListening => Port is not null;

    /// <summary>The <c>ws://127.0.0.1</c> address plugins connect to; <c>null</c> when no port was found.</summary>
    public string? Url => Port is { } port ? $"ws://127.0.0.1:{port}{Path}" : null;
}
