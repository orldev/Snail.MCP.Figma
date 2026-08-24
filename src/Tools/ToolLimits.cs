namespace Snail.MCP.Figma.Tools;

/// <summary>Limits quoted in tool descriptions; the attribute needs a const string, so a guard test keeps the numbers in sync.</summary>
public static class ToolLimits
{
    /// <summary>Depth cap of <c>figma_get_file_for_plugin</c>; deeper REST responses reach megabytes.</summary>
    public const int MaxPluginViewDepth = 5;
}
