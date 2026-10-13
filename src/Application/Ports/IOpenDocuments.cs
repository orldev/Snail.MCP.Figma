namespace Snail.MCP.Figma.Application.Ports;

/// <summary>One agent's view of the documents open in the editor: the file its commands go to, the remarks left for it and where it stands in each console.</summary>
/// <remarks>The target moves only on the agent's word. It used to follow the human's clicks, and with the plugin open in several files a click meant for another agent redirected a running build into the wrong file.</remarks>
public interface IOpenDocuments
{
    IReadOnlyCollection<IOpenDocument> Sessions { get; }

    bool HasConnection { get; }

    /// <summary>File the commands go to; <c>null</c> until the agent names one or a single connected file makes the choice obvious.</summary>
    /// <remarks>It stays while that file's plugin is away: the agent's commands then fail and name the file, and are never moved to another file, which would be somebody else's.</remarks>
    string? ActiveFileKey { get; }

    /// <summary>The session the human interacted with most recently — what a read about the human means while no target is chosen.</summary>
    IOpenDocument? LastTouched { get; }

    /// <summary>Claims the file as the target and tells its panel; <c>false</c> when no plugin runs there.</summary>
    /// <remarks>Not named after binding: ASP.NET treats a public <c>BindAsync</c> on any type used as an endpoint parameter as a request binder, and the health endpoint takes the registry.</remarks>
    Task<bool> TryClaimAsync(string fileKey, CancellationToken cancellationToken = default);

    /// <summary>Live session by key, or the target without a key; <c>null</c> when none is connected.</summary>
    /// <remarks>Several connected files and no target is a question only the agent can answer, so it throws <see cref="EditorTargetAmbiguousException"/> naming them instead of guessing.</remarks>
    IOpenDocument? Resolve(string? fileKey = null);

    /// <summary>Remarks the human left for this agent in the document and it has not read yet.</summary>
    /// <remarks>A remark typed while a build runs stops it between steps, which is the only moment a build can end without leaving half a node behind; one typed in the quiet waits here until the agent asks, because that is when a human actually reviews the result. The queue is the agent's, not the file's: two agents in one file each hear only what was said to them.</remarks>
    Remarks RemarksIn(IOpenDocument document);

    /// <summary>Tells the document's panel how many remarks are still waiting there.</summary>
    Task PublishRemarksWaitingAsync(IOpenDocument document, CancellationToken cancellationToken);

    /// <summary>The console mark this agent reads the document from: lines added before it were cleared by this agent.</summary>
    /// <remarks>The console belongs to the file, so one agent clearing it must not empty it for another; clearing moves the mark instead.</remarks>
    long ConsoleMark(IOpenDocument document);

    /// <summary>Starts this agent's console of the document from now.</summary>
    void ClearConsole(IOpenDocument document);

    /// <summary>Names of the other agents working in the document, of this server and of others.</summary>
    IReadOnlyList<string> OthersIn(IOpenDocument document);
}
