namespace Snail.MCP.Figma.Application.Diff;

/// <summary>In-memory cache of past-version snapshots; past versions are immutable, HEAD is never cached.</summary>
public sealed class VersionSnapshotCache(int maxEntries = 50)
{
    private readonly int _maxEntries = Math.Max(1, maxEntries);
    private readonly LinkedList<string> _recency = new();
    private readonly Dictionary<string, JsonNode> _store = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) { return _store.Count; } }
    }

    /// <summary>Request key; <c>null</c> for HEAD, which is never cached.</summary>
    public static string? MakeKey(string fileKey, string? versionId, int? depth, IReadOnlyList<string>? nodeIds)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return null;
        }

        var ids = nodeIds is { Count: > 0 }
            ? string.Join(',', nodeIds.Order(StringComparer.Ordinal))
            : string.Empty;

        return $"{fileKey}:{versionId}:{(depth is null ? "full" : depth.ToString())}:{ids}";
    }

    public JsonNode? Get(string? key)
    {
        if (key is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_store.TryGetValue(key, out var value))
            {
                return null;
            }

            _recency.Remove(key);
            _recency.AddLast(key);

            return value;
        }
    }

    public void Set(string? key, JsonNode? value)
    {
        if (key is null || value is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_store.ContainsKey(key))
            {
                _recency.Remove(key);
            }

            _store[key] = value;
            _recency.AddLast(key);

            while (_store.Count > _maxEntries && _recency.First is { } oldest)
            {
                _store.Remove(oldest.Value);
                _recency.RemoveFirst();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _store.Clear();
            _recency.Clear();
        }
    }
}
