namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Document open in the live editor with our extension inside.</summary>
public interface IOpenDocument
{
    /// <summary>Identifier of the connection the extension opened.</summary>
    string ConnectionId { get; }

    /// <summary>Key of the file, as reported by the extension; <c>null</c> until it identifies the file.</summary>
    string? FileKey { get; }

    /// <summary>Name of the file, as reported by the extension; <c>null</c> until it identifies the file.</summary>
    string? FileName { get; }

    /// <summary>Name of the page open in the editor.</summary>
    string? CurrentPage { get; }

    /// <summary>Id of the page open in the editor, as of the human's last page switch; <c>null</c> before the first one.</summary>
    string? CurrentPageId { get; }

    /// <summary>Kind of editor the document is open in, such as <c>figma</c>, <c>figjam</c> or <c>slides</c>.</summary>
    string? EditorType { get; }

    /// <summary>Version the connected extension reported.</summary>
    string? PluginVersion { get; }

    /// <summary>Whether the connection to the extension is still open.</summary>
    bool IsOpen { get; }

    /// <summary>When the human last selected, switched a page or wrote a remark here.</summary>
    DateTimeOffset LastInteractionAt { get; }

    /// <summary>Names of the other agents working in this file, as the panel reports them.</summary>
    /// <remarks>Servers do not see each other; only the panel, connected to all of them, knows who else has claimed the file.</remarks>
    IReadOnlyList<string> Peers { get; }

    /// <summary>Last known selection as sent by the editor.</summary>
    JsonNode? Selection { get; }

    /// <summary>Recent console output of the extension sandbox.</summary>
    RingBuffer<JsonNode> ConsoleLogs { get; }

    /// <summary>Recent document-change events reported by the editor.</summary>
    RingBuffer<JsonNode> DocumentChanges { get; }

    /// <summary>Recent description and annotation edits, which published version snapshots never expose.</summary>
    RingBuffer<JsonNode> MetadataChanges { get; }

    /// <summary>Tells the panel how many remarks are still waiting for the agents working in this file.</summary>
    /// <remarks>The queue lives on the server: without this the panel would light its wave on Enter and never learn that the agent has read anything.</remarks>
    Task PublishRemarksWaitingAsync(int waiting, CancellationToken cancellationToken);

    /// <summary>Tells the panel which step of a stepped build is running; step zero means it is over.</summary>
    /// <remarks>The plugin receives every step as its own command and cannot count them, so the share can only come from here.</remarks>
    Task PublishBuildProgressAsync(int step, int total, CancellationToken cancellationToken);

    /// <summary>Tells the panel whether an agent of this server works in the file, so remarks typed there reach it and nobody else.</summary>
    Task PublishTargetBoundAsync(bool bound, CancellationToken cancellationToken);

    /// <summary>Short summary for tool responses.</summary>
    JsonObject Describe();
}
