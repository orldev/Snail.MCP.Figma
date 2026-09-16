namespace Snail.MCP.Figma.Tools.Prompts;

/// <summary>Human-facing texts: errors and hints in tool responses, seen by the model after the call; tool descriptions live in <see cref="ToolDescriptions"/>.</summary>
public static class Messages
{
    public const string PluginNotConnected = "The Snail Bridge plugin is not connected.";

    public const string HowToConnectPlugin =
        "Open the file in Figma Desktop and run the plugin: Plugins → Development → Snail Bridge. " +
        "csharp_diagnose shows the path to the manifest.";

    public const string CheckNodeIdHint = "Check the node id and its type.";

    public const string PluginWindowHint = "Make sure the plugin window in Figma is open and not minimised.";

    public const string PassFileUrlHint = "Pass fileUrl explicitly.";

    public const string HowToChooseTarget =
        "Call figma_navigate with the link of the file you work in; figma_list_open_files shows the connected files. " +
        "The target then stays until you navigate elsewhere.";

    /// <summary>READ-channel hints: a read always has a REST fallback.</summary>
    public static class ReadFallback
    {
        public const string NotConnected =
            "Open the file in Figma Desktop and run the plugin, or retry with source=\"rest\".";

        public const string CommandFailed = "Check the node id, or retry with source=\"rest\".";

        public const string Timeout =
            "Make sure the plugin window in Figma is open and not minimised, or retry with source=\"rest\".";
    }

    public static string UnknownSource(string? source) => $"Unknown source '{source}'.";

    public static readonly string AllowedSources =
        $"Allowed: {string.Join(", ", Enum.GetNames<ReadSource>().Select(n => n.ToLowerInvariant()))}.";

    public static string PluginUnavailable(string fileKey) => $"The plugin is not connected to file {fileKey}.";

    public const string PluginUnavailableHint =
        "Open the file in Figma Desktop and run the Snail Bridge plugin, or call with source=\"rest\".";
}
