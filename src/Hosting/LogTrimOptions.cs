namespace Snail.MCP.Figma.Hosting;

/// <summary>Which log file to keep within a size, how large it may grow, and how often to look.</summary>
public sealed record LogTrimOptions(string Path, long LimitBytes, TimeSpan Cadence);
