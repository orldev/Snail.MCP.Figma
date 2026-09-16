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

    /// <summary>When the human last selected, switched a page or wrote a remark here.</summary>
    DateTimeOffset LastInteractionAt { get; }

    /// <summary>Names of the other agents working in this file, as the panel reports them.</summary>
    /// <remarks>Servers do not see each other; only the panel, connected to all of them, knows who else has claimed the file.</remarks>
    IReadOnlyList<string> Peers { get; }

    /// <summary>Last known selection as sent by the editor.</summary>
    JsonNode? Selection { get; }

    RingBuffer<JsonNode> ConsoleLogs { get; }

    RingBuffer<JsonNode> DocumentChanges { get; }

    RingBuffer<JsonNode> MetadataChanges { get; }

    /// <summary>Remarks left from the plugin panel and not yet read.</summary>
    /// <remarks>A remark typed while a build runs stops it between steps, which is the only moment a build can end without leaving half a node behind; one typed in the quiet waits here until the agent asks, because that is when a human actually reviews the result.</remarks>
    Remarks Remarks { get; }

    /// <summary>Tells the panel how many remarks are still waiting.</summary>
    /// <remarks>The queue lives on the server: without this the panel would light its wave on Enter and never learn that the agent has read anything.</remarks>
    Task PublishRemarksWaitingAsync(CancellationToken cancellationToken);

    /// <summary>Tells the panel which step of a stepped build is running; step zero means it is over.</summary>
    /// <remarks>The plugin receives every step as its own command and cannot count them, so the share can only come from here.</remarks>
    Task PublishBuildProgressAsync(int step, int total, CancellationToken cancellationToken);

    /// <summary>Tells the panel whether this agent works in the file, so remarks typed there reach it and nobody else.</summary>
    Task PublishTargetBoundAsync(bool bound, CancellationToken cancellationToken);

    /// <summary>Short summary for tool responses.</summary>
    JsonObject Describe();
}
