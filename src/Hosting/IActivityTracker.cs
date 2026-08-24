namespace Snail.MCP.Figma.Hosting;

/// <summary>Timestamp of the last MCP tool invocation — the "still in use" signal for the idle watchdog.</summary>
public interface IActivityTracker
{
    /// <summary>UTC instant of the most recent <see cref="Touch"/>.</summary>
    DateTime LastActivityUtc { get; }

    /// <summary>Records that a tool was just invoked.</summary>
    void Touch();
}
