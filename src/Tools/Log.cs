namespace Snail.MCP.Figma.Tools;

/// <summary>What the tools say when a channel fails and they fall back to another.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line below the level in force costs nothing, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "Plugin did not return node {NodeId}, reading through REST: {Message}")]
    internal static partial void NodeReadThroughRest(ILogger logger, string? nodeId, string? message);

    [LoggerMessage(EventId = 502, Level = LogLevel.Warning, Message = "Design-system extraction hit a file-system error")]
    internal static partial void ExtractionFileSystemError(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 503, Level = LogLevel.Warning, Message = "Design-system extraction failed")]
    internal static partial void ExtractionFailed(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 504, Level = LogLevel.Information, Message = "The design system kit was compressed: {Original} KB → {Compressed} KB, level {Level}")]
    internal static partial void KitCompressed(ILogger logger, string? original, string? compressed, string? level);

    [LoggerMessage(EventId = 505, Level = LogLevel.Information, Message = "Plugin did not return variables, trying REST: {Message}")]
    internal static partial void VariablesTriedThroughRest(ILogger logger, string? message);

    [LoggerMessage(EventId = 506, Level = LogLevel.Warning, Message = "Tokens not received: {Message}")]
    internal static partial void KitTokensMissing(ILogger logger, string? message);

    [LoggerMessage(EventId = 507, Level = LogLevel.Warning, Message = "Components not received: {Message}")]
    internal static partial void KitComponentsMissing(ILogger logger, string? message);

    [LoggerMessage(EventId = 508, Level = LogLevel.Warning, Message = "Node batch could not be read: {Message}")]
    internal static partial void KitNodeBatchMissing(ILogger logger, string? message);

    [LoggerMessage(EventId = 509, Level = LogLevel.Warning, Message = "Component images not received: {Message}")]
    internal static partial void KitImagesMissing(ILogger logger, string? message);

    [LoggerMessage(EventId = 510, Level = LogLevel.Warning, Message = "Styles not received: {Message}")]
    internal static partial void KitStylesMissing(ILogger logger, string? message);

    [LoggerMessage(EventId = 511, Level = LogLevel.Warning, Message = "Bridge unavailable: {Message}")]
    internal static partial void BridgeUnavailable(ILogger logger, string? message);

    [LoggerMessage(EventId = 512, Level = LogLevel.Warning, Message = "Empty rectangle {NodeId} stayed behind after a failed placement: {Message}")]
    internal static partial void RectangleLeftBehind(ILogger logger, string? nodeId, string? message);

    [LoggerMessage(EventId = 513, Level = LogLevel.Warning, Message = "Plugin did not return the file tree, falling back to REST: {Message}")]
    internal static partial void FileTreeThroughRest(ILogger logger, string? message);

    [LoggerMessage(EventId = 514, Level = LogLevel.Warning, Message = "Plugin did not return node {NodeId}, falling back to REST: {Message}")]
    internal static partial void NodeThroughRest(ILogger logger, string? nodeId, string? message);

    [LoggerMessage(EventId = 515, Level = LogLevel.Warning, Message = "Plugin did not return variables, falling back to REST: {Message}")]
    internal static partial void VariablesThroughRest(ILogger logger, string? message);

    [LoggerMessage(EventId = 516, Level = LogLevel.Warning, Message = "Published variables unavailable: {Message}")]
    internal static partial void PublishedVariablesUnavailable(ILogger logger, string? message);

    [LoggerMessage(EventId = 517, Level = LogLevel.Warning, Message = "Plugin did not return styles, falling back to REST: {Message}")]
    internal static partial void StylesThroughRest(ILogger logger, string? message);

    [LoggerMessage(EventId = 518, Level = LogLevel.Warning, Message = "Render quota exhausted — node {NodeId} captured through the plugin")]
    internal static partial void CapturedThroughPlugin(ILogger logger, string? nodeId);

    [LoggerMessage(EventId = 519, Level = LogLevel.Warning, Message = "Plugin could not capture node {NodeId}: {Message}")]
    internal static partial void CaptureFailed(ILogger logger, string? nodeId, string? message);

    [LoggerMessage(EventId = 520, Level = LogLevel.Warning, Message = "Figma API returned an error")]
    internal static partial void ApiError(ILogger logger, Exception failure);

    [LoggerMessage(EventId = 521, Level = LogLevel.Warning, Message = "Variables REST unavailable: {Message}")]
    internal static partial void VariablesRestUnavailable(ILogger logger, string? message);

    [LoggerMessage(EventId = 522, Level = LogLevel.Warning, Message = "History paging stopped at page {Page}: {Message}")]
    internal static partial void HistoryPagingStopped(ILogger logger, int page, string? message);

    [LoggerMessage(EventId = 523, Level = LogLevel.Warning, Message = "Version metadata could not be read fully: {Message}")]
    internal static partial void VersionMetadataIncomplete(ILogger logger, string? message);
}
