namespace Snail.MCP.Figma.Application.Extraction;

/// <summary>What the design system kit says about the sections it could not fill and the compression it applied.</summary>
internal static partial class Log
{
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
}
