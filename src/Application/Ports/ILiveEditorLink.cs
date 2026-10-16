namespace Snail.MCP.Figma.Application.Ports;

/// <summary>State of the live-editor link for diagnostics, without access to the transport.</summary>
public interface ILiveEditorLink
{
    /// <summary>Whether the link accepts editor connections.</summary>
    bool IsListening { get; }

    /// <summary>Address shown by diagnostics; <c>null</c> until the link is up.</summary>
    string? Address { get; }
}
