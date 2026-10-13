namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>What the bridge to the Figma plugin says: connections, commands, the installed plugin, the ports it takes.</summary>
/// <remarks>
/// Generated rather than written through the logging extensions: a line below the level in force costs nothing, where the
/// extensions boxed and formatted their arguments first. The messages are the ones the server has always written.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 101, Level = LogLevel.Warning, Message = "Failed to prune old screenshots in {Dir}")]
    internal static partial void ScreenshotsNotPruned(ILogger logger, Exception failure, string? dir);

    [LoggerMessage(EventId = 102, Level = LogLevel.Debug, Message = "→ plugin {Method} (id={Id}, file={FileKey})")]
    internal static partial void CommandSent(ILogger logger, string? method, string? id, string? fileKey);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "Failed to install the plugin into {Target}")]
    internal static partial void PluginNotInstalled(ILogger logger, Exception failure, string? target);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "No plugin/ directory next to the assembly — the WRITE channel will be unavailable")]
    internal static partial void PluginNotBundled(ILogger logger);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Plugin updated in {Target} ({Count} files). Import manifest.json in Figma: Plugins → Development → Import plugin from manifest")]
    internal static partial void PluginUpdated(ILogger logger, string? target, int count);

    [LoggerMessage(EventId = 106, Level = LogLevel.Warning, Message = "Rejected WebSocket connection from origin {Origin}")]
    internal static partial void OriginRejected(ILogger logger, string? origin);

    [LoggerMessage(EventId = 107, Level = LogLevel.Information, Message = "Plugin connection closed: {Message}")]
    internal static partial void ConnectionClosed(ILogger logger, string? message);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning, Message = "Could not parse a plugin message ({Length} bytes)")]
    internal static partial void MessageNotParsed(ILogger logger, Exception failure, int length);

    [LoggerMessage(EventId = 109, Level = LogLevel.Debug, Message = "Unhandled plugin event: {Type}")]
    internal static partial void EventUnhandled(ILogger logger, string? type);

    [LoggerMessage(EventId = 110, Level = LogLevel.Information, Message = "A remark from {FileKey} was left for another agent: this one works in {Target}")]
    internal static partial void RemarkForAnotherAgent(ILogger logger, string? fileKey, string? target);

    [LoggerMessage(EventId = 111, Level = LogLevel.Information, Message = "The human stepped into the build in {FileKey}")]
    internal static partial void HumanSteppedIn(ILogger logger, string? fileKey);

    [LoggerMessage(EventId = 112, Level = LogLevel.Warning, Message = "FILE_INFO without fileKey — session stays unidentified")]
    internal static partial void FileInfoWithoutKey(ILogger logger);

    [LoggerMessage(EventId = 113, Level = LogLevel.Information, Message = "Plugin {Running} differs from the installed bundle {Installed} — showing the update banner")]
    internal static partial void PluginOutdated(ILogger logger, string? running, string? installed);

    [LoggerMessage(EventId = 114, Level = LogLevel.Information, Message = "Reading {Bytes} bytes of {Path} for the editor")]
    internal static partial void ImageRead(ILogger logger, long bytes, string? path);

    [LoggerMessage(EventId = 115, Level = LogLevel.Debug, Message = "{Method}: invalidated {Nodes} nodes and {Ancestors} ancestors")]
    internal static partial void NodesInvalidated(ILogger logger, string? method, int nodes, int ancestors);

    [LoggerMessage(EventId = 116, Level = LogLevel.Information, Message = "Plugin connected: file {FileName} ({FileKey}), {Count} sessions total")]
    internal static partial void PluginConnected(ILogger logger, string? fileName, string? fileKey, int count);

    [LoggerMessage(EventId = 117, Level = LogLevel.Information, Message = "Plugin disconnected: {FileKey}, {Count} sessions left")]
    internal static partial void PluginDisconnected(ILogger logger, string? fileKey, int count);

    [LoggerMessage(EventId = 118, Level = LogLevel.Information, Message = "Target file: {FileName} ({FileKey})")]
    internal static partial void TargetChosen(ILogger logger, string? fileName, string? fileKey);

    [LoggerMessage(EventId = 119, Level = LogLevel.Debug, Message = "The panel of {FileKey} could not be told the agent is here")]
    internal static partial void PanelNotTold(ILogger logger, Exception failure, string? fileKey);

    [LoggerMessage(EventId = 120, Level = LogLevel.Information, Message = "WebSocket hub will use port {Port}")]
    internal static partial void PortChosen(ILogger logger, int port);

    [LoggerMessage(EventId = 121, Level = LogLevel.Debug, Message = "Port {Port} is busy, trying the next one")]
    internal static partial void PortBusy(ILogger logger, int port);

    [LoggerMessage(EventId = 122, Level = LogLevel.Warning, Message = "All ports {Start}–{End} are busy: the WRITE channel will not start. Usually these are other server instances — close the extra ones.")]
    internal static partial void PortsExhausted(ILogger logger, int start, int end);

    [LoggerMessage(EventId = 123, Level = LogLevel.Debug, Message = "Sibling servers alive on ports {Ports}")]
    internal static partial void SiblingsAlive(ILogger logger, string? ports);

    [LoggerMessage(EventId = 124, Level = LogLevel.Debug, Message = "Could not push PORTS_ALIVE to {ConnectionId}: {Message}")]
    internal static partial void PortsNotPushed(ILogger logger, string? connectionId, string? message);

    [LoggerMessage(EventId = 125, Level = LogLevel.Information, Message = "Agent {Name} ({Id}) arrived")]
    internal static partial void AgentArrived(ILogger logger, string? name, string? id);

    [LoggerMessage(EventId = 126, Level = LogLevel.Information, Message = "Agent {Name} ({Id}) left")]
    internal static partial void AgentLeft(ILogger logger, string? name, string? id);

    [LoggerMessage(EventId = 127, Level = LogLevel.Warning, Message = "A plugin message from {FileKey} could not be handled and was skipped")]
    internal static partial void MessageFailed(ILogger logger, Exception failure, string? fileKey);

    [LoggerMessage(EventId = 128, Level = LogLevel.Warning, Message = "The agents' files in {Path} could not be read; every agent starts without one")]
    internal static partial void AgentTargetsUnreadable(ILogger logger, Exception failure, string? path);

    [LoggerMessage(EventId = 129, Level = LogLevel.Warning, Message = "The agents' files could not be written to {Path}; after a restart the agents will have to name their files again")]
    internal static partial void AgentTargetsNotSaved(ILogger logger, Exception failure, string? path);

    [LoggerMessage(EventId = 130, Level = LogLevel.Information, Message = "Agent {Name} is back in {FileName} ({FileKey})")]
    internal static partial void AgentReturned(ILogger logger, string? name, string? fileName, string? fileKey);

    [LoggerMessage(EventId = 131, Level = LogLevel.Information, Message = "Agent {Id} is named after its folder: {Name}")]
    internal static partial void AgentNamed(ILogger logger, string? id, string? name);
}
