namespace Snail.MCP.Figma.Application.Diff;

/// <summary>What the version history says while it walks Figma's pages.</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 522, Level = LogLevel.Warning, Message = "History paging stopped at page {Page}: {Message}")]
    internal static partial void HistoryPagingStopped(ILogger logger, int page, string? message);

    [LoggerMessage(EventId = 523, Level = LogLevel.Warning, Message = "Version metadata could not be read fully: {Message}")]
    internal static partial void VersionMetadataIncomplete(ILogger logger, string? message);
}
