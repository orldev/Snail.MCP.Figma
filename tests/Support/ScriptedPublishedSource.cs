using Snail.MCP.Figma.Application.Ports;

namespace Snail.MCP.Figma.Tests.Support;

/// <summary>Fake REST with version history: serves <c>/versions</c> pages and version-bound snapshots while counting calls, for tests about call cost.</summary>
public sealed class ScriptedPublishedSource : IPublishedDocumentSource, IRenderSource, IDocumentHistory, IDocumentCommentary, ILibraryCatalog
{
    private const string Head = "current";

    /// <summary>Versions newest first — the order Figma returns them.</summary>
    public List<JsonObject> Versions { get; } = [];

    /// <summary>Node snapshot at a version keyed <c>version|nodeId</c>; HEAD is version <c>current</c>.</summary>
    public Dictionary<string, JsonNode> NodeSnapshots { get; } = new(StringComparer.Ordinal);

    /// <summary>File document snapshot at a version; HEAD is version <c>current</c>.</summary>
    public Dictionary<string, JsonNode> FileSnapshots { get; } = new(StringComparer.Ordinal);

    public int VersionListCalls { get; private set; }

    public int NodeCalls { get; private set; }

    public int FileCalls { get; private set; }

    public string HeadVersionId { get; set; } = "v-head";

    /// <summary>Versions per page — Figma serves up to 50.</summary>
    public int PageSize { get; set; } = 50;

    public bool IsAvailable => true;

    public static JsonObject Version(string id, string? label, string? handle = "Ann", string? createdAt = null) =>
        new()
        {
            ["id"] = id,
            ["label"] = label,
            ["description"] = string.Empty,
            ["created_at"] = createdAt ?? "2026-01-01T00:00:00Z",
            ["user"] = new JsonObject { ["handle"] = handle },
        };

    /// <summary>COMPONENT_SET with given properties and children — the typical figma_blame_node target.</summary>
    public static JsonObject ComponentSet(
        string id,
        string name,
        IEnumerable<string>? properties = null,
        IEnumerable<string>? childIds = null)
    {
        var definitions = new JsonObject();

        foreach (var property in properties ?? [])
        {
            definitions[property] = new JsonObject { ["type"] = "BOOLEAN", ["defaultValue"] = false };
        }

        var children = new JsonArray();

        foreach (var childId in childIds ?? [])
        {
            children.Add(new JsonObject { ["id"] = childId, ["name"] = childId, ["type"] = "COMPONENT" });
        }

        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["type"] = "COMPONENT_SET",
            ["componentPropertyDefinitions"] = definitions,
            ["children"] = children,
        };
    }

    public void PutNode(string versionId, string nodeId, JsonNode document) =>
        NodeSnapshots[$"{versionId}|{nodeId}"] = document;

    public void PutFile(string versionId, JsonNode document) =>
        FileSnapshots[versionId] = document;

    public Task<JsonNode> GetDocumentAsync(string fileKey, DocumentReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        FileCalls++;
        var versionId = options?.Version ?? Head;

        return Task.FromResult<JsonNode>(new JsonObject
        {
            ["name"] = "Design System",
            ["version"] = options?.Version ?? HeadVersionId,
            ["lastModified"] = "2026-01-10T00:00:00Z",
            ["document"] = FileSnapshots.GetValueOrDefault(versionId)?.DeepClone() ?? new JsonObject(),
        });
    }

    public Task<JsonNode> GetNodesAsync(string fileKey, IReadOnlyList<string> nodeIds, NodeReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        NodeCalls++;
        var versionId = options?.Version ?? Head;
        var nodes = new JsonObject();

        foreach (var id in nodeIds)
        {
            if (NodeSnapshots.GetValueOrDefault($"{versionId}|{id}") is { } document)
            {
                nodes[id] = new JsonObject { ["document"] = document.DeepClone() };
            }
        }

        return Task.FromResult<JsonNode>(new JsonObject
        {
            ["nodes"] = nodes,
            ["version"] = options?.Version ?? HeadVersionId,
            ["lastModified"] = "2026-01-10T00:00:00Z",
        });
    }

    public Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!endpoint.Contains("/versions", StringComparison.Ordinal))
        {
            return Task.FromResult<JsonNode>(new JsonObject());
        }

        VersionListCalls++;

        var after = ReadQueryValue(endpoint, "after");
        var start = 0;

        if (after is not null)
        {
            var index = Versions.FindIndex(version => version["id"]?.GetValue<string>() == after);
            start = index < 0 ? Versions.Count : index + 1;
        }

        var page = new JsonArray();

        for (var i = start; i < Versions.Count && page.Count < PageSize; i++)
        {
            page.Add(Versions[i].DeepClone());
        }

        var hasNext = start + page.Count < Versions.Count;

        return Task.FromResult<JsonNode>(new JsonObject
        {
            ["versions"] = page,
            ["pagination"] = new JsonObject
            {
                ["next_page"] = hasNext ? "has-more" : null,
            },
        });
    }

    private static string? ReadQueryValue(string endpoint, string name)
    {
        var marker = $"{name}=";
        var at = endpoint.IndexOf(marker, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var value = endpoint[(at + marker.Length)..];
        var end = value.IndexOf('&');

        return Uri.UnescapeDataString(end < 0 ? value : value[..end]);
    }

    public Task<JsonNode> GetLocalVariablesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetPublishedVariablesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetStylesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetComponentsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetComponentSetsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> RenderAsync(string fileKey, IReadOnlyList<string> nodeIds, RenderOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject { ["images"] = new JsonObject() });

    public Task<JsonNode> PostAsync(string endpoint, JsonNode? body, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) =>
        Task.FromResult<JsonNode>(new JsonObject());

    public Task<JsonNode> GetVersionsPageAsync(string documentKey, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/versions?page_size={pageSize}{(after is null ? "" : $"&after={Uri.EscapeDataString(after)}")}", cancellationToken);

    public Task<JsonNode> GetCommentsAsync(string documentKey, bool asMarkdown, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/comments", cancellationToken);

    public Task<JsonNode> PostCommentAsync(string documentKey, JsonNode body, CancellationToken cancellationToken = default) =>
        PostAsync($"/v1/files/{documentKey}/comments", body, cancellationToken);

    public Task DeleteCommentAsync(string documentKey, string commentId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"/v1/files/{documentKey}/comments/{commentId}", cancellationToken);

    public Task<JsonNode> GetPublishedComponentAsync(string componentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/components/{componentKey}", cancellationToken);

    public Task<JsonNode> GetPublishedComponentSetAsync(string componentSetKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/component_sets/{componentSetKey}", cancellationToken);

    public Task<JsonNode> GetFileMetaAsync(string documentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/files/{documentKey}/meta", cancellationToken);

    public Task<JsonNode> GetPublishedStyleAsync(string styleKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/styles/{styleKey}", cancellationToken);

    public Task<JsonNode> GetTeamComponentsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/teams/{teamId}/components?page_size={pageSize}", cancellationToken);

    public Task<JsonNode> GetTeamComponentSetsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/v1/teams/{teamId}/component_sets?page_size={pageSize}", cancellationToken);
}
