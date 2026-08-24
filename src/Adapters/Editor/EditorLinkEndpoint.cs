namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Address of the WebSocket bridge; the port is picked once before host build and <see cref="Port"/> is empty when the whole range is busy.</summary>
public sealed record EditorLinkEndpoint(int? Port, string Path)
{
    public bool IsListening => Port is not null;

    public string? Url => Port is { } port ? $"ws://127.0.0.1:{port}{Path}" : null;
}
