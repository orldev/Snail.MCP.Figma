using System.Collections.Concurrent;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Registry of connected files; the active file follows user selection unless a target is pinned.</summary>
public sealed class OpenDocumentRegistry(
    ActiveDocumentTracker activeFile,
    ILogger<OpenDocumentRegistry> logger)
    : IOpenDocuments
{
    private readonly ConcurrentDictionary<string, EditorSession> _byFileKey = new(StringComparer.Ordinal);

    private string? _activeFileKey;
    private bool _locked;

    public IReadOnlyCollection<EditorSession> Sessions => _byFileKey.Values.ToArray();

    IReadOnlyCollection<IOpenDocument> IOpenDocuments.Sessions => Sessions;

    IOpenDocument? IOpenDocuments.Resolve(string? fileKey) => Resolve(fileKey);

    public bool HasConnection => _byFileKey.Values.Any(session => session.IsOpen);

    public string? ActiveFileKey => _activeFileKey;

    /// <summary>Target is pinned: user events no longer switch the active file.</summary>
    public bool IsLocked => _locked;

    /// <summary>Binds a session to a file after <c>FILE_INFO</c>.</summary>
    public void Register(EditorSession session, string fileKey, string? fileName)
    {
        session.FileKey = fileKey;
        session.FileName = fileName;
        _byFileKey[fileKey] = session;

        if (_activeFileKey is null && !_locked)
        {
            SetActive(fileKey);
        }

        logger.LogInformation(
            "Plugin connected: file {FileName} ({FileKey}), {Count} sessions total",
            fileName, fileKey, _byFileKey.Count);
    }

    public void Remove(EditorSession session)
    {
        if (session.FileKey is not { } fileKey)
        {
            return;
        }

        _byFileKey.TryRemove(new KeyValuePair<string, EditorSession>(fileKey, session));

        if (_activeFileKey == fileKey)
        {
            _locked = false;
            _activeFileKey = _byFileKey.Keys.FirstOrDefault();
        }

        logger.LogInformation("Plugin disconnected: {FileKey}, {Count} sessions left", fileKey, _byFileKey.Count);
    }

    /// <summary>The file became active through user interaction; a pinned target is left alone.</summary>
    public void TouchByUserInteraction(EditorSession session)
    {
        session.LastActivityAt = DateTimeOffset.UtcNow;

        if (!_locked && session.FileKey is { } fileKey)
        {
            SetActive(fileKey);
        }
    }

    /// <summary>Explicitly selects the active file and optionally pins it to the agent.</summary>
    public bool SetActiveFile(string fileKey, bool @lock = false)
    {
        if (!_byFileKey.ContainsKey(fileKey))
        {
            return false;
        }

        SetActive(fileKey);
        _locked = @lock;

        return true;
    }

    public void Unlock() => _locked = false;

    /// <summary>Session for the file, or the active one when no key is given; <c>null</c> when no plugin is connected.</summary>
    public EditorSession? Resolve(string? fileKey = null)
    {
        var key = fileKey ?? _activeFileKey;

        if (key is null)
        {
            return null;
        }

        return _byFileKey.TryGetValue(key, out var session) && session.IsOpen ? session : null;
    }

    private void SetActive(string fileKey)
    {
        if (_activeFileKey == fileKey)
        {
            return;
        }

        _activeFileKey = fileKey;
        activeFile.Remember(fileKey, _byFileKey.TryGetValue(fileKey, out var session) ? session.FileName : null);
        logger.LogInformation("Active file: {FileKey}", fileKey);
    }
}
