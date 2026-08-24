namespace Snail.MCP.Figma.Hosting;

/// <summary>How long the server may sit without tool calls and how often that is checked.</summary>
public sealed record IdleShutdownOptions(TimeSpan IdleTimeout, TimeSpan PollInterval);
