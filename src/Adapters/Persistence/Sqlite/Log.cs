namespace Snail.MCP.Figma.Adapters.Persistence.Sqlite;

/// <summary>What the SQLite snapshot cache says: opening, misses, invalidation and the failures it survives.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line below the level in force costs nothing, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 201, Level = LogLevel.Warning, Message = "Snapshot cache unavailable, running without it (next retry not before {NextRetry:HH:mm:ss} UTC)")]
    internal static partial void CacheUnavailable(ILogger logger, Exception failure, DateTimeOffset nextRetry);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "Opening the snapshot cache: {File}")]
    internal static partial void CacheOpening(ILogger logger, string? file);

    [LoggerMessage(EventId = 203, Level = LogLevel.Information, Message = "Cache ready in {ElapsedMs} ms, file {File}")]
    internal static partial void CacheReady(ILogger logger, long elapsedMs, string? file);

    [LoggerMessage(EventId = 204, Level = LogLevel.Information, Message = "Removed an older cache store: {Directory}")]
    internal static partial void LegacyStoreRemoved(ILogger logger, string? directory);

    [LoggerMessage(EventId = 205, Level = LogLevel.Warning, Message = "Failed to remove an older cache store in {Directory}")]
    internal static partial void LegacyStoreNotRemoved(ILogger logger, Exception failure, string? directory);

    [LoggerMessage(EventId = 206, Level = LogLevel.Information, Message = "File {FileKey} changed ({Old} → {New}), dropping its other snapshots")]
    internal static partial void FileChanged(ILogger logger, string? fileKey, string? @old, string? @new);

    [LoggerMessage(EventId = 207, Level = LogLevel.Warning, Message = "Failed to store the version of file {FileKey}")]
    internal static partial void VersionNotStored(ILogger logger, Exception failure, string? fileKey);

    [LoggerMessage(EventId = 208, Level = LogLevel.Warning, Message = "Failed to read the stored version of file {FileKey}")]
    internal static partial void VersionNotRead(ILogger logger, Exception failure, string? fileKey);

    [LoggerMessage(EventId = 209, Level = LogLevel.Warning, Message = "Failed to forget the version of file {FileKey}")]
    internal static partial void VersionNotForgotten(ILogger logger, Exception failure, string? fileKey);

    [LoggerMessage(EventId = 210, Level = LogLevel.Debug, Message = "Cache {FileKey}/{NodeId} expired ({ExpiresAt:O}) — miss")]
    internal static partial void SnapshotExpired(ILogger logger, string? fileKey, string? nodeId, DateTimeOffset expiresAt);

    [LoggerMessage(EventId = 211, Level = LogLevel.Debug, Message = "Cache {FileKey}/{NodeId} was taken on version {Cached}, current is {Current} — miss")]
    internal static partial void SnapshotOutdated(ILogger logger, string? fileKey, string? nodeId, string? cached, string? current);

    [LoggerMessage(EventId = 212, Level = LogLevel.Warning, Message = "Cache read failed, falling back to Figma")]
    internal static partial void CacheReadFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 213, Level = LogLevel.Warning, Message = "Cache write failed, continuing without caching")]
    internal static partial void CacheWriteFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 214, Level = LogLevel.Warning, Message = "Node invalidation failed")]
    internal static partial void NodeInvalidationFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 215, Level = LogLevel.Information, Message = "Cache of file {FileKey} dropped, {Count} documents removed")]
    internal static partial void FileCacheDropped(ILogger logger, string? fileKey, int count);

    [LoggerMessage(EventId = 216, Level = LogLevel.Warning, Message = "Dropping the file cache failed")]
    internal static partial void FileCacheNotDropped(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 217, Level = LogLevel.Debug, Message = "Swept {Count} expired snapshots")]
    internal static partial void ExpiredSwept(ILogger logger, int count);

    [LoggerMessage(EventId = 218, Level = LogLevel.Debug, Message = "Render cache {FileKey}/{NodeId} expired — miss")]
    internal static partial void RenderExpired(ILogger logger, string? fileKey, string? nodeId);

    [LoggerMessage(EventId = 219, Level = LogLevel.Debug, Message = "Render {FileKey}/{NodeId} was made on version {Cached}, current is {Current} — miss")]
    internal static partial void RenderOutdated(ILogger logger, string? fileKey, string? nodeId, string? cached, string? current);

    [LoggerMessage(EventId = 220, Level = LogLevel.Warning, Message = "Render cache read failed, falling back to Figma")]
    internal static partial void RenderReadFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 221, Level = LogLevel.Warning, Message = "Storing the render link in the cache failed")]
    internal static partial void RenderWriteFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 222, Level = LogLevel.Warning, Message = "Render invalidation failed")]
    internal static partial void RenderInvalidationFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 223, Level = LogLevel.Information, Message = "Render cache of file {FileKey} dropped, {Count} removed")]
    internal static partial void RenderCacheDropped(ILogger logger, string? fileKey, int count);

    [LoggerMessage(EventId = 224, Level = LogLevel.Warning, Message = "Dropping the render cache failed")]
    internal static partial void RenderCacheNotDropped(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 225, Level = LogLevel.Error, Message = "The snapshot cache failed to open")]
    internal static partial void CacheNotOpened(ILogger logger, Exception failure);
}
