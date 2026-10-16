using System.Text;
using System.Text.Json;
using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma.Application.Sanitization;

/// <summary>Cleans a Figma response for the LLM: strips vector geometry and defaults, bounds depth and node count, deduplicates styles.</summary>
public sealed class NodeSanitizer(SanitizationOptions options, ILogger<NodeSanitizer> logger)
{
    /// <summary>Fields whose values take part in style deduplication.</summary>
    private static readonly HashSet<string> StyleBearingKeys = new(StringComparer.Ordinal)
    {
        "fills", "strokes", "effects", "style", "styles", "layoutGrids",
    };

    private static readonly JsonSerializerOptions CompactJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Request with defaults from server configuration.</summary>
    public SanitizationRequest DefaultRequest => new()
    {
        Profile = options.Profile,
        MaxDepth = options.DefaultDepth,
        MaxNodes = options.DefaultMaxNodes,
    };

    /// <summary>Sanitizes the tree and, when metrics are on, logs the savings to stderr.</summary>
    public SanitizationResult Sanitize(JsonNode source, SanitizationRequest? request = null, string? label = null)
    {
        request ??= DefaultRequest;

        var bytesIn = MeasureBytes(source);

        if (request.IsPassthrough)
        {
            var passthrough = new SanitizationResult(source.DeepClone(), bytesIn, bytesIn, 0, 0, 0);
            LogMetric(passthrough, label, request);

            return passthrough;
        }

        var context = new WalkContext(request);
        var cleaned = Visit(source, depth: 0, context) ?? new JsonObject();

        var deduplicated = request.ShouldDeduplicateStyles ? ApplyStyleDictionary(cleaned, context) : 0;

        var result = new SanitizationResult(
            cleaned,
            bytesIn,
            MeasureBytes(cleaned),
            context.NodesEmitted,
            context.NodesOmitted,
            deduplicated);

        LogMetric(result, label, request);

        return result;
    }

    private JsonNode? Visit(JsonNode? node, int depth, WalkContext context)
    {
        switch (node)
        {
            case JsonObject obj:
                return VisitObject(obj, depth, context);

            case JsonArray array:
                {
                    var result = new JsonArray();

                    foreach (var item in array)
                    {
                        var visited = Visit(item, depth, context);

                        if (visited is not null)
                        {
                            result.Add(visited);
                        }
                    }

                    return result;
                }

            default:
                return node?.DeepClone();
        }
    }

    private JsonNode? VisitObject(JsonObject source, int depth, WalkContext context)
    {
        var dropped = SanitizationRules.DroppedKeys(context.Request.Profile);
        if (IsSceneNode(source))
        {
            context.NodesEmitted++;
        }

        var result = new JsonObject();

        foreach (var (key, value) in source)
        {
            if (dropped.Contains(key) && !SanitizationRules.IsProtected(key))
            {
                continue;
            }

            if (context.Request.Profile == SanitizationProfile.Aggressive)
            {
                if (SanitizationRules.IsDefaultValue(key, value) || SanitizationRules.IsAllZeroOverrides(key, value))
                {
                    continue;
                }

                if (!SanitizationRules.IsProtected(key) && IsEmptyContainer(value))
                {
                    continue;
                }
            }

            if (key == "children" && value is JsonArray children)
            {
                var visitedChildren = VisitChildren(children, depth, context, result);

                if (visitedChildren is not null)
                {
                    result["children"] = visitedChildren;
                }

                continue;
            }

            var visited = Visit(value, depth, context);

            if (visited is null)
            {
                continue;
            }

            if (context.Request.ShouldDeduplicateStyles && StyleBearingKeys.Contains(key) && !IsEmptyContainer(visited))
            {
                context.RegisterStyleCandidate(result, key, visited);
            }

            result[key] = visited;
        }

        return result;
    }

    /// <summary>Progressive disclosure: at the depth or budget limit children are not expanded, but their count is reported.</summary>
    private JsonArray? VisitChildren(JsonArray children, int depth, WalkContext context, JsonObject parent)
    {
        if (context.Request.MaxDepth is { } maxDepth && depth >= maxDepth)
        {
            context.NodesOmitted += CountNodes(children);
            parent["childrenOmitted"] = children.Count;
            parent["childrenOmittedReason"] = "maxDepth";

            return null;
        }

        var result = new JsonArray();
        var omitted = 0;

        foreach (var child in children)
        {
            if (context.Request.MaxNodes is { } maxNodes && context.NodesEmitted >= maxNodes)
            {
                omitted += CountNodes(child);
                continue;
            }

            var visited = Visit(child, depth + 1, context);

            if (visited is not null)
            {
                result.Add(visited);
            }
        }

        if (omitted > 0)
        {
            context.NodesOmitted += omitted;
            parent["childrenOmitted"] = omitted;
            parent["childrenOmittedReason"] = "maxNodes";
        }

        return result;
    }

    /// <summary>Repeated fill and effect sets move into <c>$styles</c>, leaving a short reference in place.</summary>
    private static int ApplyStyleDictionary(JsonNode root, WalkContext context)
    {
        if (root is not JsonObject rootObject)
        {
            return 0;
        }

        var dictionary = new JsonObject();
        var index = 0;

        foreach (var (signature, occurrences) in context.StyleCandidates)
        {
            if (occurrences.Count < 2)
            {
                continue;
            }

            var id = $"s{++index}";
            dictionary[id] = JsonNode.Parse(signature);

            foreach (var (owner, key) in occurrences)
            {
                owner[key] = JsonValue.Create($"$styles.{id}");
            }
        }

        if (index > 0)
        {
            rootObject["$styles"] = dictionary;
            rootObject["$stylesHint"] = "Values of the form \"$styles.sN\" are references to the entries of the same name in $styles.";
        }

        return index;
    }

    private void LogMetric(SanitizationResult result, string? label, SanitizationRequest request)
    {
        if (!options.LogMetrics)
        {
            return;
        }

        Log.Sanitized(logger, label ?? "-", request.Profile, result.ToMetricString());
    }

    private static bool IsEmptyContainer(JsonNode? node) => node switch
    {
        JsonArray array => array.Count == 0,
        JsonObject obj => obj.Count == 0,
        _ => false,
    };

    /// <summary>Whether the object is a node of the canvas, the unit the node budget counts.</summary>
    /// <remarks>A variable binding carries an id and a type too (<c>VARIABLE_ALIAS</c>); counted as a node, a frame with three bindings spent four of the budget and real children were cut as <c>maxNodes</c>.</remarks>
    private static bool IsSceneNode(JsonObject candidate) =>
        candidate.ContainsKey("id") &&
        candidate["type"] is JsonValue type &&
        !IsVariableAlias(type);

    /// <summary>Compares the parsed text in place: reading it out as a string allocated one per node, measured as 40 KB more on <c>SanitizeFile</c>.</summary>
    private static bool IsVariableAlias(JsonValue type) =>
        type.TryGetValue<JsonElement>(out var parsed)
            ? parsed.ValueKind == JsonValueKind.String && parsed.ValueEquals("VARIABLE_ALIAS")
            : type.TryGetValue<string>(out var kind) && kind == "VARIABLE_ALIAS";

    private static int CountNodes(JsonNode? node) => node switch
    {
        JsonObject obj => (IsSceneNode(obj) ? 1 : 0) +
                          (obj["children"] is JsonArray children ? CountNodes(children) : 0),
        JsonArray array => array.Sum(CountNodes),
        _ => 0,
    };

    private static long MeasureBytes(JsonNode node) =>
        Encoding.UTF8.GetByteCount(node.ToJsonString(CompactJson));

    /// <summary>State of one walk: counters and deduplication candidates.</summary>
    private sealed class WalkContext(SanitizationRequest request)
    {
        public SanitizationRequest Request { get; } = request;

        public int NodesEmitted { get; set; }

        public int NodesOmitted { get; set; }

        /// <summary>Canonical style JSON → places where it occurred.</summary>
        public Dictionary<string, List<(JsonObject Owner, string Key)>> StyleCandidates { get; } = new(StringComparer.Ordinal);

        public void RegisterStyleCandidate(JsonObject owner, string key, JsonNode value)
        {
            var signature = value.ToJsonString(CompactJson);

            if (signature.Length <= 24)
            {
                return;
            }

            if (!StyleCandidates.TryGetValue(signature, out var occurrences))
            {
                occurrences = [];
                StyleCandidates[signature] = occurrences;
            }

            occurrences.Add((owner, key));
        }
    }
}
