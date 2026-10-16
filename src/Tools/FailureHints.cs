using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>What a tool family suggests after each kind of failure: the message is the failure's own, the hint is the way out.</summary>
/// <remarks>The read tools can fall back on REST and say so; the write tools cannot, so their hints only point at the plugin.</remarks>
public sealed record FailureHints(string NotConnected, string CommandFailed, string Timeout, string? BadArgument)
{
    /// <summary>For tools that only work through the plugin.</summary>
    public static FailureHints Editor { get; } = new(
        Messages.HowToConnectPlugin,
        Messages.CheckNodeIdHint,
        Messages.PluginWindowHint,
        BadArgument: null);

    /// <summary>For the read tools, which can take the same answer from REST.</summary>
    public static FailureHints Read { get; } = new(
        Messages.ReadFallback.NotConnected,
        Messages.ReadFallback.CommandFailed,
        Messages.ReadFallback.Timeout,
        Messages.PassFileUrlHint);
}
