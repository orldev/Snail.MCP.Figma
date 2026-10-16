using Snail.MCP.Figma.Application.Editor;

namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Live editor: sees unsaved edits, costs no quota and is the only writer; transport and snapshot invalidation belong to the adapter.</summary>
public interface ILiveEditor
{
    /// <summary>Whether at least one document has the extension connected.</summary>
    bool IsConnected { get; }

    /// <summary>This agent's view of the documents open in the editor.</summary>
    IOpenDocuments Documents { get; }

    /// <summary>Side-effect-free command — a read through the editor.</summary>
    Task<JsonNode?> ReadAsync(
        EditorCommand command,
        JsonObject? parameters = null,
        string? fileKey = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>Mutation; only the editor reply confirms the write.</summary>
    Task<JsonNode?> ApplyAsync(
        EditorCommand command,
        JsonObject parameters,
        string? fileKey = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>Compact tree of a node, or of the whole document when <paramref name="nodeId"/> is null.</summary>
    Task<JsonNode?> ReadTreeAsync(string fileKey, string? nodeId, int depth, int maxNodes, CancellationToken cancellationToken = default);

    /// <summary>Local styles in the published-source shape, so callers need not tell sources apart.</summary>
    Task<JsonNode?> ReadStylesAsync(string fileKey, CancellationToken cancellationToken = default);

    /// <summary>Drops every snapshot of the document — for edits of unknown scope.</summary>
    Task InvalidateWholeFileAsync(string fileKey, CancellationToken cancellationToken = default);
}
