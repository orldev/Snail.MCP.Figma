namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Registry of documents open in the editor and the active one — targets of the WRITE channel.</summary>
public interface IOpenDocuments
{
    IReadOnlyCollection<IOpenDocument> Sessions { get; }

    bool HasConnection { get; }

    string? ActiveFileKey { get; }

    /// <summary>The active document is pinned: editor events do not switch it.</summary>
    bool IsLocked { get; }

    bool SetActiveFile(string fileKey, bool @lock = false);

    void Unlock();

    /// <summary>Live session by key, or the active one without a key; <c>null</c> when none.</summary>
    IOpenDocument? Resolve(string? fileKey = null);
}
