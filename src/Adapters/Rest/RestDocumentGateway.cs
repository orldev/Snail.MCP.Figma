using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Adapters.Rest;

/// <summary>HTTP client for the Figma REST API: token-dependent auth header, retries on 429 and uniform error wrapping.</summary>
public sealed class RestDocumentGateway : IPublishedDocumentSource, IRenderSource, IDocumentHistory, IDocumentCommentary, ILibraryCatalog, IApiIdentity
{
    public const string HttpClientName = "figma";
    private const string ApiBase = "https://api.figma.com/v1";
    private const int MaxAttempts = 3;

    /// <summary>Cap on honoring <c>Retry-After</c>: a pathological header must not hang the caller.</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServerConfig _config;
    private readonly ILogger<RestDocumentGateway> _logger;
    private readonly QuotaGate _rateLimit;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RestDocumentGateway(
        IHttpClientFactory httpClientFactory,
        ServerConfig config,
        ILogger<RestDocumentGateway> logger,
        QuotaGate rateLimit)
        : this(httpClientFactory, config, logger, rateLimit, delay: null)
    {
    }

    /// <summary>Test entry point with a replaceable delay between retries.</summary>
    internal RestDocumentGateway(
        IHttpClientFactory httpClientFactory,
        ServerConfig config,
        ILogger<RestDocumentGateway> logger,
        QuotaGate rateLimit,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
        _rateLimit = rateLimit;
        _delay = delay ?? Task.Delay;
    }

    public bool IsAvailable => _config.HasAccessToken;

    public Task<JsonNode> GetDocumentAsync(string fileKey, DocumentReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = new QueryBuilder();
        query.Add("version", options?.Version);
        query.Add("ids", options?.Ids is { Count: > 0 } ids ? string.Join(',', ids.Select(NodeId.Normalize)) : null);
        query.Add("depth", options?.Depth);
        query.Add("geometry", options?.Geometry);

        if (options?.BranchData == true)
        {
            query.Add("branch_data", "true");
        }

        return GetAsync($"/files/{Uri.EscapeDataString(fileKey)}{query}", cancellationToken);
    }

    public async Task<JsonNode> GetNodesAsync(
        string fileKey,
        IReadOnlyList<string> nodeIds,
        NodeReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = nodeIds.Select(NodeId.Normalize).ToArray();

        var query = new QueryBuilder();
        query.Add("ids", string.Join(',', normalized));
        query.Add("version", options?.Version);
        query.Add("depth", options?.Depth);
        query.Add("geometry", options?.Geometry);

        var response = await GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/nodes{query}", cancellationToken)
            .ConfigureAwait(false);

        AliasDashedIds(response["nodes"] as JsonObject, nodeIds, normalized);

        return response;
    }

    public async Task<JsonNode> GetLocalVariablesAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/variables/local", cancellationToken)
            .ConfigureAwait(false);

        return Unwrap(response);
    }

    public async Task<JsonNode> GetPublishedVariablesAsync(string fileKey, CancellationToken cancellationToken = default)
    {
        var response = await GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/variables/published", cancellationToken)
            .ConfigureAwait(false);

        return Unwrap(response);
    }

    public Task<JsonNode> GetStylesAsync(string fileKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/styles", cancellationToken);

    public Task<JsonNode> GetComponentsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/components", cancellationToken);

    public Task<JsonNode> GetComponentSetsAsync(string fileKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/files/{Uri.EscapeDataString(fileKey)}/component_sets", cancellationToken);

    public async Task<JsonNode> RenderAsync(
        string fileKey,
        IReadOnlyList<string> nodeIds,
        RenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = nodeIds.Select(NodeId.Normalize).ToArray();

        var query = new QueryBuilder();
        query.Add("ids", string.Join(',', normalized));
        query.Add("scale", options?.Scale);
        query.Add("format", options?.Format);
        query.Add("contents_only", options?.ContentsOnly);
        query.Add("svg_outline_text", options?.SvgOutlineText);
        query.Add("svg_include_id", options?.SvgIncludeId);
        query.Add("svg_simplify_stroke", options?.SvgSimplifyStroke);

        var response = await GetAsync($"/images/{Uri.EscapeDataString(fileKey)}{query}", cancellationToken)
            .ConfigureAwait(false);

        AliasDashedIds(response["images"] as JsonObject, nodeIds, normalized);

        return response;
    }

    public Task<JsonNode> GetVersionsPageAsync(string documentKey, int pageSize, string? after, CancellationToken cancellationToken = default)
    {
        var endpoint = $"/files/{Uri.EscapeDataString(documentKey)}/versions?page_size={pageSize}";

        if (!string.IsNullOrWhiteSpace(after))
        {
            endpoint += $"&after={Uri.EscapeDataString(after)}";
        }

        return GetAsync(endpoint, cancellationToken);
    }

    public Task<JsonNode> GetCommentsAsync(string documentKey, bool asMarkdown, CancellationToken cancellationToken = default) =>
        GetAsync($"/files/{Uri.EscapeDataString(documentKey)}/comments{(asMarkdown ? "?as_md=true" : string.Empty)}", cancellationToken);

    public Task<JsonNode> PostCommentAsync(string documentKey, JsonNode body, CancellationToken cancellationToken = default) =>
        PostAsync($"/files/{Uri.EscapeDataString(documentKey)}/comments", body, cancellationToken);

    public Task DeleteCommentAsync(string documentKey, string commentId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"/files/{Uri.EscapeDataString(documentKey)}/comments/{Uri.EscapeDataString(commentId)}", cancellationToken);

    public Task<JsonNode> GetFileMetaAsync(string documentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/files/{Uri.EscapeDataString(documentKey)}/meta", cancellationToken);

    public Task<JsonNode> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync("/me", cancellationToken);

    public Task<JsonNode> GetPublishedStyleAsync(string styleKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/styles/{Uri.EscapeDataString(styleKey)}", cancellationToken);

    public Task<JsonNode> GetTeamComponentsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/teams/{Uri.EscapeDataString(teamId)}/components?page_size={pageSize}{AfterCursor(after)}", cancellationToken);

    public Task<JsonNode> GetTeamComponentSetsPageAsync(string teamId, int pageSize, string? after, CancellationToken cancellationToken = default) =>
        GetAsync($"/teams/{Uri.EscapeDataString(teamId)}/component_sets?page_size={pageSize}{AfterCursor(after)}", cancellationToken);

    private static string AfterCursor(string? after) =>
        string.IsNullOrWhiteSpace(after) ? string.Empty : $"&after={Uri.EscapeDataString(after)}";

    public Task<JsonNode> GetPublishedComponentAsync(string componentKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/components/{Uri.EscapeDataString(componentKey)}", cancellationToken);

    public Task<JsonNode> GetPublishedComponentSetAsync(string componentSetKey, CancellationToken cancellationToken = default) =>
        GetAsync($"/component_sets/{Uri.EscapeDataString(componentSetKey)}", cancellationToken);

    /// <summary>Arbitrary GET — for the adapter and its tests only; tools go through ports.</summary>
    internal Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, endpoint, body: null, cancellationToken);

    internal Task<JsonNode> PostAsync(string endpoint, JsonNode? body, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, endpoint, body, cancellationToken);

    internal Task<JsonNode> DeleteAsync(string endpoint, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, endpoint, body: null, cancellationToken);

    /// <summary>One API request with retries on 429, shared by reads and comment writes.</summary>
    private async Task<JsonNode> SendAsync(
        HttpMethod method,
        string endpoint,
        JsonNode? body,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new PublishedSourceException(
                SourceFailure.Unauthorized,
                "SNAIL_MCP_FIGMA_ACCESS_TOKEN is not set. Create a personal token at figma.com → Settings → Security → Personal access tokens.",
                isAuthError: true);
        }

        if (_rateLimit.RemainingFor(endpoint) is { } blocked)
        {
            _logger.LogWarning(
                "Bucket {Bucket} is closed by exhausted quota for another {Remaining} — not sending {Endpoint}",
                QuotaGate.BucketOf(endpoint), PublishedSourceException.Describe(blocked), endpoint);

            throw new PublishedSourceException(
                SourceFailure.RateLimited,
                $"Figma API error (429): the quota of endpoint '{QuotaGate.BucketOf(endpoint)}' is exhausted, " +
                $"the request was not sent (local circuit breaker). " +
                $"Figma rate-limited the requests and asks to wait {PublishedSourceException.Describe(blocked)}.",
                isAuthError: false,
                retryAfter: blocked);
        }

        using var client = CreateClient();
        var url = ApiBase + endpoint;

        HttpResponseMessage response;

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, url);

            if (body is not null)
            {
                request.Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            }

            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
            {
                break;
            }

            var retryAfter = ReadRetryAfter(response);

            if (retryAfter > MaxRetryAfter)
            {
                _logger.LogWarning(
                    "Figma API rate-limited the request (429) and asks to wait {RetryAfter} — retries are pointless, "
                    + "failing immediately. The plan quota is exhausted; the endpoint reopens after that period.",
                    retryAfter);
                break;
            }

            var delay = retryAfter ?? TimeSpan.FromSeconds(attempt);
            _logger.LogWarning(
                "Figma API rate-limited the request (429), attempt {Attempt}/{MaxAttempts}, waiting {DelayMs} ms",
                attempt, MaxAttempts, (int)delay.TotalMilliseconds);

            response.Dispose();
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var finalRetryAfter = ReadRetryAfter(response);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && finalRetryAfter > MaxRetryAfter)
                {
                    _rateLimit.Block(endpoint, finalRetryAfter.Value);
                }

                throw BuildException(response.StatusCode, payload, finalRetryAfter);
            }

            _rateLimit.Release(endpoint);

            if (string.IsNullOrWhiteSpace(payload))
            {
                return new JsonObject { ["success"] = true };
            }

            return JsonNode.Parse(payload)
                ?? throw new PublishedSourceException(Classify(response.StatusCode), "Figma returned an empty response");
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var token = _config.AccessToken!;

        if (token.StartsWith("figu_", StringComparison.Ordinal))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            client.DefaultRequestHeaders.Remove("X-Figma-Token");
            client.DefaultRequestHeaders.Add("X-Figma-Token", token);
        }

        return client;
    }

    /// <summary>How long Figma asks to wait; <c>null</c> when the header is missing or meaningless.</summary>
    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("retry-after", out var values) &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }

    private static PublishedSourceException BuildException(HttpStatusCode status, string body, TimeSpan? retryAfter)
    {
        var message = $"Figma API error ({(int)status}): {body}";

        var isAuthError = status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden &&
                          (body.Contains("invalid token", StringComparison.OrdinalIgnoreCase) ||
                           body.Contains("token expired", StringComparison.OrdinalIgnoreCase) ||
                           body.Contains("unauthorized", StringComparison.OrdinalIgnoreCase));

        if (isAuthError)
        {
            message += " — the Figma token has expired or is invalid. Create a new one at figma.com → Settings → Security → " +
                       "Personal access tokens and update SNAIL_MCP_FIGMA_ACCESS_TOKEN.";
        }
        else switch (status)
        {
            case HttpStatusCode.TooManyRequests:
                message += retryAfter is { } wait
                    ? $" Figma rate-limited the requests and asks to wait {PublishedSourceException.Describe(wait)}."
                    : " Figma rate-limited the requests — wait and retry.";
                break;
            case HttpStatusCode.Forbidden:
                message += " This may be a plan limitation rather than a token problem (the Variables API, for example, is Enterprise-only).";
                break;
        }

        return new PublishedSourceException(Classify(status), message, isAuthError, retryAfter);
    }

    /// <summary>The HTTP status stays here; only a transport-agnostic failure reason leaves the adapter.</summary>
    private static SourceFailure Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => SourceFailure.Unauthorized,
        HttpStatusCode.Forbidden => SourceFailure.Forbidden,
        HttpStatusCode.NotFound => SourceFailure.NotFound,
        HttpStatusCode.TooManyRequests => SourceFailure.RateLimited,
        _ => SourceFailure.Unavailable,
    };

    /// <summary>Unwraps <c>{status, error, meta:{…}}</c> responses into <c>meta</c> so the shape is uniform.</summary>
    private static JsonNode Unwrap(JsonNode response) => response["meta"] ?? response;

    /// <summary>Figma keys responses by colon-form ids; dash-form ids passed by the caller get an alias.</summary>
    private static void AliasDashedIds(JsonObject? map, IReadOnlyList<string> original, IReadOnlyList<string> normalized)
    {
        if (map is null)
        {
            return;
        }

        for (var i = 0; i < original.Count; i++)
        {
            if (original[i] == normalized[i] || map.ContainsKey(original[i]))
            {
                continue;
            }

            if (map.TryGetPropertyValue(normalized[i], out var value))
            {
                map[original[i]] = value?.DeepClone();
            }
        }
    }

    /// <summary>Builds a query string, skipping empty values.</summary>
    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        public void Add(string name, int? value)
        {
            if (value.HasValue)
            {
                _parts.Add($"{name}={value.Value.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        public void Add(string name, double? value)
        {
            if (value.HasValue)
            {
                _parts.Add($"{name}={value.Value.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        public void Add(string name, bool? value)
        {
            if (value.HasValue)
            {
                _parts.Add($"{name}={(value.Value ? "true" : "false")}");
            }
        }

        public override string ToString() => _parts.Count == 0 ? string.Empty : $"?{string.Join('&', _parts)}";
    }
}
