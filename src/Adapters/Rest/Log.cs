namespace Snail.MCP.Figma.Adapters.Rest;

/// <summary>What the REST gateway says about the quota Figma grants it.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line below the level in force costs nothing, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 301, Level = LogLevel.Warning, Message = "Bucket {Bucket} is closed by exhausted quota for another {Remaining} — not sending {Endpoint}")]
    internal static partial void BucketClosed(ILogger logger, string? bucket, string? remaining, string? endpoint);

    [LoggerMessage(EventId = 302, Level = LogLevel.Warning, Message = "Figma API rate-limited the request (429) and asks to wait {RetryAfter} — retries are pointless, failing immediately. The plan quota is exhausted; the endpoint reopens after that period.")]
    internal static partial void QuotaExhausted(ILogger logger, TimeSpan? retryAfter);

    [LoggerMessage(EventId = 303, Level = LogLevel.Warning, Message = "Figma API rate-limited the request (429), attempt {Attempt}/{MaxAttempts}, waiting {DelayMs} ms")]
    internal static partial void RateLimitedRetrying(ILogger logger, int attempt, int maxAttempts, int delayMs);

    [LoggerMessage(EventId = 304, Level = LogLevel.Warning, Message = "Figma API answered {Status}, attempt {Attempt}/{MaxAttempts}, waiting {DelayMs} ms")]
    internal static partial void ServerErrorRetrying(ILogger logger, int status, int attempt, int maxAttempts, int delayMs);

    [LoggerMessage(EventId = 305, Level = LogLevel.Warning, Message = "Figma API was not reached, attempt {Attempt}/{MaxAttempts}: {Reason}")]
    internal static partial void TransportFailedRetrying(ILogger logger, int attempt, int maxAttempts, string reason);
}
