using System.Collections.Concurrent;

namespace Snail.MCP.Figma.Adapters.Rest;

/// <summary>Quota circuit breaker: a 429 with a long <c>Retry-After</c> closes the endpoint's bucket so further requests fail locally without a round trip.</summary>
public sealed class QuotaGate : IQuotaStatus
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _blockedUntil = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public QuotaGate()
        : this(now: null)
    {
    }

    /// <summary>Test entry point with a replaceable clock.</summary>
    internal QuotaGate(Func<DateTimeOffset>? now)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Bucket of an endpoint: first path segment plus sub-resource, e.g. <c>files</c> or <c>files/versions</c>; the file key is not part of it.</summary>
    public static string BucketOf(string endpoint)
    {
        var path = endpoint.AsSpan();

        var query = path.IndexOf('?');

        if (query >= 0)
        {
            path = path[..query];
        }

        var segments = path.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length switch
        {
            0 => "/",
            1 or 2 => segments[0],
            _ => $"{segments[0]}/{segments[2]}",
        };
    }

    /// <summary>Remaining block time for the endpoint's bucket; <c>null</c> when open.</summary>
    public TimeSpan? RemainingFor(string endpoint)
    {
        var bucket = BucketOf(endpoint);

        if (!_blockedUntil.TryGetValue(bucket, out var until))
        {
            return null;
        }

        var remaining = until - _now();

        if (remaining > TimeSpan.Zero)
        {
            return remaining;
        }

        _blockedUntil.TryRemove(bucket, out _);

        return null;
    }

    /// <summary>Closes the bucket until <paramref name="retryAfter"/> elapses.</summary>
    public void Block(string endpoint, TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return;
        }

        var until = _now() + retryAfter;
        _blockedUntil.AddOrUpdate(BucketOf(endpoint), until, (_, existing) => existing > until ? existing : until);
    }

    /// <summary>Opens the bucket: a successful reply proves the quota is back even if the clock disagrees.</summary>
    public void Release(string endpoint) => _blockedUntil.TryRemove(BucketOf(endpoint), out _);

    /// <summary>Active blocks — for csharp_diagnose.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> ActiveBlocks
    {
        get
        {
            var now = _now();

            return _blockedUntil
                .Where(pair => pair.Value > now)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }
    }
}
