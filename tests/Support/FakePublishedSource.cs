using Snail.MCP.Figma.Application.Ports;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Fake Figma API that counts calls to prove the cache really saves requests.</summary>
public sealed class FakePublishedSource(JsonNode file, string version = "v1") : IPublishedDocumentSource, IRenderSource, IDocumentHistory, IDocumentCommentary, ILibraryCatalog, IApiIdentity
{
    private readonly JsonNode _file = file;

    public string Version { get; set; } = version;

    /// <summary>Tree of a specific node; without an entry the shared file is returned.</summary>
    public Dictionary<string, JsonNode> NodeDocuments { get; } = new(StringComparer.Ordinal);

    public int FileCalls { get; private set; }

    public int VersionCalls { get; private set; }

    public int NodeCalls { get; private set; }

    /// <summary>Fail reads — how Figma behaves when quota is exhausted (429).</summary>
    public bool ShouldFailFileRequests { get; set; }

    private static PublishedSourceException RateLimited() => new(
        SourceFailure.RateLimited,
        "Figma API error (429): Rate limit exceeded",
        isAuthError: false,
        retryAfter: TimeSpan.FromHours(13));

    /// <summary>The Figma envelope carries version/lastModified — the reader takes the version from it.</summary>
    private JsonObject Envelope(JsonNode payload)
    {
        var envelope = payload.DeepClone().AsObject();
        envelope["version"] = Version;
        envelope["lastModified"] = DateTimeOffset.UtcNow.ToString("O");

        return envelope;
    }

    public bool IsAvailable => true;

    public Task<JsonNode> GetDocumentAsync(string fileKey, DocumentReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        FileCalls++;

        if (ShouldFailFileRequests)
        {
            return Task.FromException<JsonNode>(RateLimited());
        }

        return Task.FromResult<JsonNode>(Envelope(_file));
    }

    public Task<JsonNode> GetNodesAsync(string fileKey, IReadOnlyList<string> nodeIds, NodeReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        NodeCalls++;

        if (ShouldFailFileRequests)
        {
            return Task.FromException<JsonNode>(RateLimited());
        }

        var nodes = new JsonObject();

        foreach (var id in nodeIds)
        {
            nodes[id] = new JsonObject
            {
                ["document"] = NodeDocuments.GetValueOrDefault(id)?.DeepClone() ?? _file.DeepClone(),
            };
        }

        return Task.FromResult<JsonNode>(new JsonObject
        {
            ["nodes"] = nodes,
            ["version"] = Version,
            ["lastModified"] = DateTimeOffset.UtcNow.ToString("O"),
        });
    }

    /// <summary>How often variables were requested via REST — plugin-first must never get here.</summary>
    public int VariableCalls { get; private set; }

    /// <summary>How often styles were requested via REST.</summary>
    public int StyleCalls { get; private set; }

    public Task<JsonNode> GetLocalVariablesAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        VariableCalls++;

        return Task.FromResult<JsonNode>(new JsonObject());
    }

    public Task<JsonNode> GetPublishedVariablesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetStylesAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        StyleCalls++;

        return Task.FromResult<JsonNode>(new JsonObject());
    }

    /// <summary><c>/v1/files/{key}/components</c> response — variant keys of a set come from it.</summary>
    public JsonNode? ComponentsResponse { get; set; }

    public Task<JsonNode> GetComponentsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(ComponentsResponse?.DeepClone() ?? new JsonObject());

    public Task<JsonNode> GetComponentSetsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    /// <summary>How often rendering was requested; shows whether the render cache saves anything.</summary>
    public int ImageCalls { get; private set; }

    /// <summary>Nodes sent to Figma for rendering — in batches, in call order.</summary>
    public List<IReadOnlyList<string>> ImageRequests { get; } = [];

    /// <summary>Fail rendering: how Figma behaves when the expensive bucket is exhausted.</summary>
    public bool ShouldFailImageRequests { get; set; }

    public Task<JsonNode> RenderAsync(string fileKey, IReadOnlyList<string> nodeIds, RenderOptions? options = null, CancellationToken cancellationToken = default)
    {
        ImageCalls++;
        ImageRequests.Add([.. nodeIds]);

        if (ShouldFailImageRequests)
        {
            return Task.FromException<JsonNode>(RateLimited());
        }

        var images = new JsonObject();

        foreach (var nodeId in nodeIds)
        {
            images[nodeId] = $"https://figma.test/{fileKey}/{nodeId}/{options?.Format ?? "png"}@{options?.Scale ?? 1}/{ImageCalls}";
        }

        return Task.FromResult<JsonNode>(new JsonObject { ["images"] = images });
    }

    /// <summary>Responses to arbitrary GETs by exact path.</summary>
    public Dictionary<string, JsonNode> Endpoints { get; } = new(StringComparer.Ordinal);

    /// <summary>Path prefixes Figma answers with 404 — to exercise fallback paths.</summary>
    public HashSet<string> NotFound { get; } = new(StringComparer.Ordinal);

    /// <summary>Path prefixes Figma answers with 403 — how a missing or foreign key really reads.</summary>
    public HashSet<string> Forbidden { get; } = new(StringComparer.Ordinal);

    /// <summary>File comments served by <c>/v1/files/{key}/comments</c>.</summary>
    public JsonArray Comments { get; } = [];

    /// <summary>All requested paths in order — shows which endpoint was asked first.</summary>
    public List<string> RequestedEndpoints { get; } = [];

    public string? LastDeletedEndpoint { get; private set; }

    public Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        RequestedEndpoints.Add(endpoint);

        if (NotFound.Any(prefix => endpoint.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return Task.FromException<JsonNode>(new PublishedSourceException(
                SourceFailure.NotFound,
                "Figma API error (404): Not found",
                isAuthError: false));
        }

        if (Forbidden.Any(prefix => endpoint.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return Task.FromException<JsonNode>(new PublishedSourceException(
                SourceFailure.Forbidden,
                "Figma API error (403): Forbidden",
                isAuthError: false));
        }

        if (Endpoints.TryGetValue(endpoint, out var canned))
        {
            return Task.FromResult(canned.DeepClone());
        }

        if (endpoint.Contains("/comments", StringComparison.Ordinal))
        {
            return Task.FromResult<JsonNode>(new JsonObject { ["comments"] = Comments.DeepClone() });
        }

        return Task.FromResult<JsonNode>(new JsonObject());
    }

    public Task<JsonNode> PostAsync(string endpoint, JsonNode? body, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject { ["endpoint"] = endpoint, ["body"] = body?.DeepClone() });

    public Task<JsonNode> DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        LastDeletedEndpoint = endpoint;

        return Task.FromResult<JsonNode>(new JsonObject { ["endpoint"] = endpoint, ["deleted"] = true });
    }

    public Task<JsonNode> GetVersionsPageAsync(string documentKey, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/versions?page_size={pageSize}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", cancellationToken);

    public Task<JsonNode> GetCommentsAsync(string documentKey, bool asMarkdown, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/comments{(asMarkdown ? "?as_md=true" : "")}", cancellationToken);

    public Task<JsonNode> PostCommentAsync(string documentKey, JsonNode body, CancellationToken cancellationToken = default) =>
        PostAsync($"/v1/files/{documentKey}/comments", body, cancellationToken);

    public Task DeleteCommentAsync(string documentKey, string commentId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"/v1/files/{documentKey}/comments/{Uri.EscapeDataString(commentId)}", cancellationToken);

    public Task<JsonNode> GetPublishedComponentAsync(string componentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/components/{Uri.EscapeDataString(componentKey)}", cancellationToken);

    public Task<JsonNode> GetPublishedComponentSetAsync(string componentSetKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/component_sets/{Uri.EscapeDataString(componentSetKey)}", cancellationToken);

    public Task<JsonNode> GetFileMetaAsync(string documentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/meta", cancellationToken);

    public Task<JsonNode> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync("/v1/me", cancellationToken);

    public Task<JsonNode> GetPublishedStyleAsync(string styleKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/styles/{Uri.EscapeDataString(styleKey)}", cancellationToken);

    public Task<JsonNode> GetTeamComponentsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/teams/{teamId}/components?page_size={pageSize}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", cancellationToken);

    public Task<JsonNode> GetTeamComponentSetsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/teams/{teamId}/component_sets?page_size={pageSize}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", cancellationToken);
}
