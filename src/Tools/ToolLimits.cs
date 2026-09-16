namespace Snail.MCP.Figma.Tools;

/// <summary>Limits quoted in tool descriptions; the attribute needs a const string, so a guard test keeps the numbers in sync.</summary>
public static class ToolLimits
{
    /// <summary>Depth cap of <c>figma_get_file_for_plugin</c>; deeper REST responses reach megabytes.</summary>
    public const int MaxPluginViewDepth = 5;

    /// <summary>Steps one <c>figma_execute_steps</c> call runs.</summary>
    /// <remarks>A longer list stops being a build the caller watches and becomes a script that holds the tool call open; globals survive between calls, so the rest continues in the next one.</remarks>
    public const int MaxSteps = 60;

    /// <summary>Longest pause between steps, in milliseconds.</summary>
    /// <remarks>At the cap a full build still fits inside a couple of minutes, which is the point past which a client gives up on the call.</remarks>
    public const int MaxStepDelayMs = 2000;

    /// <summary>Longest a read may hold the call open waiting for a remark, in milliseconds.</summary>
    /// <remarks>MCP has no way for a server to wake an agent, so waiting inside the call is the only way the human is heard within a second instead of at the agent's next turn; past a minute the client gives up on the call and the wait costs more than it buys.</remarks>
    public const int MaxRemarkWaitMs = 60000;
}
