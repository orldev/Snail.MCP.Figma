namespace Snail.MCP.Figma.Domain;

/// <summary>Why the published source returned nothing; callers branch on this, not on HTTP codes.</summary>
public enum SourceFailure
{
    /// <summary>Token missing, expired or invalid.</summary>
    Unauthorized,

    /// <summary>Access denied: no rights to the document or a plan restriction.</summary>
    Forbidden,

    NotFound,

    /// <summary>Quota exhausted; <see cref="PublishedSourceException.RetryAfter"/> says how long to wait.</summary>
    RateLimited,

    /// <summary>The source is unavailable or replied unintelligibly.</summary>
    Unavailable,
}

/// <summary>Published-source (REST) failure with the reason classified by the adapter.</summary>
public sealed class PublishedSourceException(
    SourceFailure failure,
    string message,
    bool isAuthError = false,
    TimeSpan? retryAfter = null)
    : Exception(message)
{
    public SourceFailure Failure { get; } = failure;

    /// <summary>Token expired or invalid, as opposed to a plan-related Forbidden that is not an auth problem.</summary>
    public bool IsAuthError { get; } = isAuthError;

    /// <summary>How long the source asks to wait when quota is exhausted.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;

    /// <summary>Hint for the caller: what to do about this particular failure.</summary>
    public string Hint
    {
        get
        {
            if (IsAuthError)
            {
                return "Update SNAIL_MCP_FIGMA_ACCESS_TOKEN in the MCP server configuration.";
            }

            if (Failure == SourceFailure.RateLimited)
            {
                return RetryAfter is { } wait && wait > TimeSpan.FromMinutes(5)
                    ? $"The Figma plan quota is exhausted, the endpoint reopens in about {Describe(wait)}. " +
                      "Retrying earlier is pointless; cached snapshots keep being served."
                    : "Retry the request in a few seconds.";
            }

            return "Check the file key and your access rights to it.";
        }
    }

    /// <summary>Human-readable duration in hours, minutes or seconds.</summary>
    public static string Describe(TimeSpan value) => value.TotalHours >= 1
        ? $"{value.TotalHours:0.#} h"
        : value.TotalMinutes >= 1
            ? $"{value.TotalMinutes:0.#} min"
            : $"{value.TotalSeconds:0} s";

    /// <summary>Forbidden/NotFound may mean no access to the document rather than a broken server.</summary>
    public bool IsAccessDenied => Failure is SourceFailure.Forbidden or SourceFailure.NotFound;
}
