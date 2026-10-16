using Snail.MCP.Figma.Configuration;

namespace Snail.MCP.Figma;

/// <summary>What reading, rendering, sanitizing and the idle watch say.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line below the level in force costs nothing, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "cache hit {FileKey}/{NodeId} variant={Variant} ({Bytes} bytes)")]
    internal static partial void CacheHit(ILogger logger, string? fileKey, string? nodeId, string? variant, long bytes);

    [LoggerMessage(EventId = 402, Level = LogLevel.Warning, Message = "Figma unavailable ({Error}) — serving {FileKey}/{NodeId} from the cache of {Fetched:u}")]
    internal static partial void ServedFromCache(ILogger logger, string? error, string? fileKey, string? nodeId, DateTimeOffset fetched);

    [LoggerMessage(EventId = 403, Level = LogLevel.Debug, Message = "Render {FileKey}: all {Count} nodes from cache, Figma not called")]
    internal static partial void RenderAllFromCache(ILogger logger, string? fileKey, int count);

    [LoggerMessage(EventId = 404, Level = LogLevel.Information, Message = "sanitize[{Label}] profile={Profile} {Metric}")]
    internal static partial void Sanitized(ILogger logger, string? label, SanitizationProfile profile, string? metric);

    [LoggerMessage(EventId = 405, Level = LogLevel.Debug, Message = "DOCUMENT_CHANGE {FileKey}: {Changed} nodes, {Removed} documents removed")]
    internal static partial void DocumentChanged(ILogger logger, string? fileKey, int changed, int removed);

    [LoggerMessage(EventId = 406, Level = LogLevel.Warning, Message = "No MCP tool has been called for {IdleFor}; the process looks orphaned, shutting down")]
    internal static partial void IdleShutdown(ILogger logger, TimeSpan idleFor);

    [LoggerMessage(EventId = 601, Level = LogLevel.Information, Message = "The client did not declare roots, so its agent keeps the name it gave")]
    internal static partial void RootsNotDeclared(ILogger logger);

    [LoggerMessage(EventId = 602, Level = LogLevel.Information, Message = "The client listed {Count} roots and none of them is a folder, so its agent keeps the name it gave")]
    internal static partial void NoFolderRoot(ILogger logger, int count);

    [LoggerMessage(EventId = 603, Level = LogLevel.Warning, Message = "The client did not answer roots/list within {Seconds} s")]
    internal static partial void RootsUnanswered(ILogger logger, double seconds);

    [LoggerMessage(EventId = 604, Level = LogLevel.Warning, Message = "The client refused roots/list")]
    internal static partial void RootsRefused(ILogger logger, Exception failure);
}
