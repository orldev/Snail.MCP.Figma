namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Document open in the live editor with our extension inside.</summary>
public interface IOpenDocument
{
    string ConnectionId { get; }

    string? FileKey { get; }

    string? FileName { get; }

    string? CurrentPage { get; }

    string? CurrentPageId { get; }

    string? EditorType { get; }

    string? PluginVersion { get; }

    bool IsOpen { get; }

    /// <summary>Last known selection as sent by the editor.</summary>
    JsonNode? Selection { get; }

    RingBuffer<JsonNode> ConsoleLogs { get; }

    RingBuffer<JsonNode> DocumentChanges { get; }

    RingBuffer<JsonNode> MetadataChanges { get; }

    /// <summary>Short summary for tool responses.</summary>
    JsonObject Describe();
}
