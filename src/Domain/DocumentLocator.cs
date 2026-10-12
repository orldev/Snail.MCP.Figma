using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Domain;

/// <summary>Parses a design document link into document key, branch and node.</summary>
public static partial class DocumentLocator
{
    /// <summary>A branch is its own document: for <c>/design/{file}/branch/{branch}/…</c> the effective key is the branch key.</summary>
    [GeneratedRegex(@"/(design|file|board|slides)/([a-zA-Z0-9]+)/branch/([a-zA-Z0-9]+)")]
    private static partial Regex BranchPathPattern();

    [GeneratedRegex(@"/(design|file|board|slides)/([a-zA-Z0-9]+)")]
    private static partial Regex FilePathPattern();

    /// <summary>Document key from the link; for a branch link, the branch key.</summary>
    public static string? ExtractKey(string? url) => Parse(url)?.EffectiveKey;

    /// <summary>Full link parse; <c>null</c> when it is not a document link.</summary>
    public static DocumentReference? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var nodeIdParam = query["node-id"];
        var nodeId = string.IsNullOrWhiteSpace(nodeIdParam) ? null : NodeId.Normalize(nodeIdParam);

        var branchMatch = BranchPathPattern().Match(uri.AbsolutePath);

        if (branchMatch.Success)
        {
            return new DocumentReference(branchMatch.Groups[2].Value, branchMatch.Groups[3].Value, nodeId);
        }

        var fileMatch = FilePathPattern().Match(uri.AbsolutePath);

        if (!fileMatch.Success)
        {
            return null;
        }

        var branchId = query["branch-id"];

        return new DocumentReference(
            fileMatch.Groups[2].Value,
            string.IsNullOrWhiteSpace(branchId) ? null : branchId,
            nodeId);
    }
}

/// <summary>Parsed document link.</summary>
/// <param name="Key">Key of the main document, as in its URL.</param>
/// <param name="BranchKey">Branch key when the link points into a branch.</param>
/// <param name="NodeId">Node from <c>node-id</c>, already in colon form.</param>
public sealed record DocumentReference(string Key, string? BranchKey, string? NodeId)
{
    /// <summary>Key to use with the source: the branch overrides the main document.</summary>
    public string EffectiveKey => BranchKey ?? Key;
}
