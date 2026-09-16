using System.Collections.Concurrent;
using System.Net.WebSockets;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Registry of connected files and the one the agent works in.</summary>
/// <remarks>The target moves only on the agent's word: a navigate, the only file there is, or the file the agent last named. Human clicks stamp the session and nothing else — with several agents open, a click in another file was meant for one of them, and the server cannot tell which.</remarks>
public sealed class OpenDocumentRegistry(
    ActiveDocumentTracker activeFile,
    ILogger<OpenDocumentRegistry> logger)
    : IOpenDocuments
{
    private readonly ConcurrentDictionary<string, EditorSession> _byFileKey = new(StringComparer.Ordinal);

    private string? _targetFileKey;

    public IReadOnlyCollection<EditorSession> Sessions => _byFileKey.Values.ToArray();

    IReadOnlyCollection<IOpenDocument> IOpenDocuments.Sessions => Sessions;

    IOpenDocument? IOpenDocuments.Resolve(string? fileKey) => Resolve(fileKey);

    IOpenDocument? IOpenDocuments.LastTouched => LastTouched;

    public bool HasConnection => _byFileKey.Values.Any(session => session.IsOpen);

    public string? ActiveFileKey => _targetFileKey;

    /// <summary>The open session the human interacted with most recently.</summary>
    public EditorSession? LastTouched =>
        _byFileKey.Values.Where(session => session.IsOpen).MaxBy(session => session.LastInteractionAt);

    /// <summary>Binds a session to a file after <c>FILE_INFO</c>; the only connected file becomes the target by itself.</summary>
    /// <remarks>A target file whose plugin reconnected is told again that the agent is here: the panel starts empty on every reload.</remarks>
    public async Task RegisterAsync(EditorSession session, string fileKey, string? fileName, CancellationToken cancellationToken)
    {
        session.FileKey = fileKey;
        session.FileName = fileName;
        _byFileKey[fileKey] = session;

        logger.LogInformation(
            "Plugin connected: file {FileName} ({FileKey}), {Count} sessions total",
            fileName, fileKey, _byFileKey.Count);

        if (_targetFileKey is null && _byFileKey.Count == 1)
        {
            await ClaimAsync(fileKey, cancellationToken).ConfigureAwait(false);

            return;
        }

        if (_targetFileKey == fileKey)
        {
            await session.PublishTargetBoundAsync(true, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Forgets a closed session; a target whose plugin is gone falls to the only file left, or to nobody.</summary>
    public async Task RemoveAsync(EditorSession session, CancellationToken cancellationToken)
    {
        if (session.FileKey is not { } fileKey ||
            !_byFileKey.TryRemove(new KeyValuePair<string, EditorSession>(fileKey, session)))
        {
            return;
        }

        logger.LogInformation("Plugin disconnected: {FileKey}, {Count} sessions left", fileKey, _byFileKey.Count);

        if (_targetFileKey != fileKey)
        {
            return;
        }

        _targetFileKey = null;

        if (_byFileKey.Keys.ToArray() is [var only])
        {
            await ClaimAsync(only, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Claims the file as the target and tells both panels: the new one that the agent is here, the old one that it left.</summary>
    public async Task<bool> ClaimAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        if (!_byFileKey.TryGetValue(fileKey, out var session))
        {
            return false;
        }

        var previous = _targetFileKey;
        Claim(session);

        if (previous is not null && previous != fileKey && _byFileKey.TryGetValue(previous, out var left) && left.IsOpen)
        {
            await left.PublishTargetBoundAsync(false, cancellationToken).ConfigureAwait(false);
        }

        if (session.IsOpen)
        {
            await session.PublishTargetBoundAsync(true, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Session for the file, or the target when no key is given; <c>null</c> when no plugin is connected.</summary>
    /// <remarks>Without a target the only open file, or the file the agent last named in any tool, is adopted on the spot; several files with no such hint throw, because a guess would write into somebody else's file.</remarks>
    public EditorSession? Resolve(string? fileKey = null)
    {
        if (fileKey is not null)
        {
            return Open(fileKey);
        }

        if (_targetFileKey is { } target)
        {
            return Open(target);
        }

        var open = _byFileKey.Values.Where(session => session.IsOpen).ToArray();

        if (open.Length == 0)
        {
            return null;
        }

        var chosen = open.Length == 1
            ? open[0]
            : open.FirstOrDefault(session => session.FileKey == activeFile.FileKey);

        if (chosen is null)
        {
            throw new EditorTargetAmbiguousException(Choices(open));
        }

        Claim(chosen);
        _ = AnnounceAsync(chosen);

        return chosen;
    }

    private EditorSession? Open(string fileKey) =>
        _byFileKey.TryGetValue(fileKey, out var session) && session.IsOpen ? session : null;

    private void Claim(EditorSession session)
    {
        if (_targetFileKey == session.FileKey)
        {
            return;
        }

        _targetFileKey = session.FileKey;
        activeFile.Remember(session.FileKey!, session.FileName);
        logger.LogInformation("Target file: {FileName} ({FileKey})", session.FileName, session.FileKey);
    }

    /// <summary>Tells the panel of a file adopted inside a synchronous resolve; a lost notice only delays the panel until the first command arrives.</summary>
    private async Task AnnounceAsync(EditorSession session)
    {
        try
        {
            await session.PublishTargetBoundAsync(true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogDebug(ex, "The panel of {FileKey} could not be told the agent is here", session.FileKey);
        }
    }

    private static string Choices(IEnumerable<EditorSession> open) =>
        $"Several files are connected and none is the target: {string.Join(", ", open.Select(session => $"{session.FileName ?? "unnamed"} ({session.FileKey})"))}.";
}
