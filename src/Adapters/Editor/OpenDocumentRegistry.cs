using System.Collections.Concurrent;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Registry of the files whose plugin is connected, shared by every agent of this server.</summary>
/// <remarks>What is open is a fact about the editor, the same for everyone who asks. Which file an agent works in is that agent's own business and lives in its <see cref="AgentSlot"/>.</remarks>
public sealed class OpenDocumentRegistry(ILogger<OpenDocumentRegistry> logger)
{
    private readonly ConcurrentDictionary<string, EditorSession> _byFileKey = new(StringComparer.Ordinal);

    public IReadOnlyCollection<EditorSession> Sessions => [.. _byFileKey.Values];

    public bool HasConnection => _byFileKey.Values.Any(session => session.IsOpen);

    /// <summary>The open session the human interacted with most recently.</summary>
    public EditorSession? LastTouched =>
        _byFileKey.Values.Where(session => session.IsOpen).MaxBy(session => session.LastInteractionAt);

    /// <summary>Binds a session to its file after <c>FILE_INFO</c>; a reconnecting plugin replaces the session it left behind.</summary>
    public void Register(EditorSession session, string fileKey, string? fileName)
    {
        session.FileKey = fileKey;
        session.FileName = fileName;
        _byFileKey[fileKey] = session;

        Log.PluginConnected(logger, fileName, fileKey, _byFileKey.Count);
    }

    /// <summary>Forgets a closed session, unless the file has already been taken over by a newer one.</summary>
    public void Remove(EditorSession session)
    {
        if (session.FileKey is not { } fileKey ||
            !_byFileKey.TryRemove(new KeyValuePair<string, EditorSession>(fileKey, session)))
        {
            return;
        }

        Log.PluginDisconnected(logger, fileKey, _byFileKey.Count);
    }

    /// <summary>The session registered for the file, open or not yet removed.</summary>
    public EditorSession? Find(string fileKey) =>
        _byFileKey.TryGetValue(fileKey, out var session) ? session : null;

    /// <summary>The live session of the file; <c>null</c> when its plugin is not connected.</summary>
    public EditorSession? Open(string fileKey) =>
        Find(fileKey) is { IsOpen: true } session ? session : null;
}
