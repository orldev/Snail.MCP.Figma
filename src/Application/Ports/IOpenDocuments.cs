namespace Snail.MCP.Figma.Application.Ports;

/// <summary>Registry of documents open in the editor and the one the agent's commands go to.</summary>
/// <remarks>The target moves only on the agent's word. It used to follow the human's clicks, and with the plugin open in several files a click meant for another agent redirected a running build into the wrong file.</remarks>
public interface IOpenDocuments
{
    IReadOnlyCollection<IOpenDocument> Sessions { get; }

    bool HasConnection { get; }

    /// <summary>File the commands go to; <c>null</c> until the agent names one or a single connected file makes the choice obvious.</summary>
    string? ActiveFileKey { get; }

    /// <summary>The session the human interacted with most recently — what a read about the human means while no target is chosen.</summary>
    IOpenDocument? LastTouched { get; }

    /// <summary>Claims the file as the target and tells its panel; <c>false</c> when no plugin runs there.</summary>
    /// <remarks>Not named after binding: ASP.NET treats a public <c>BindAsync</c> on any type used as an endpoint parameter as a request binder, and the health endpoint takes the registry.</remarks>
    Task<bool> TryClaimAsync(string fileKey, CancellationToken cancellationToken = default);

    /// <summary>Live session by key, or the target without a key; <c>null</c> when none is connected.</summary>
    /// <remarks>Several connected files and no target is a question only the agent can answer, so it throws <see cref="EditorTargetAmbiguousException"/> naming them instead of guessing.</remarks>
    IOpenDocument? Resolve(string? fileKey = null);
}
