namespace Snail.MCP.Figma.Tools.Prompts;

/// <summary>Human-facing texts: errors and hints in tool responses, seen by the model after the call; tool descriptions live in <see cref="ToolDescriptions"/>.</summary>
public static class Messages
{
    /// <summary>Error a tool answers with when it needs the plugin and no plugin is connected at all.</summary>
    public const string PluginNotConnected = "The Snail Bridge plugin is not connected.";

    /// <summary>Hint shown with a plugin-only failure when no plugin is connected, telling how to start the plugin.</summary>
    public const string HowToConnectPlugin =
        "Open the file in Figma Desktop and run the plugin: Plugins → Development → Snail Bridge. " +
        "csharp_diagnose shows the path to the manifest.";

    /// <summary>Hint shown when a plugin command fails, pointing at a wrong node id or node type.</summary>
    public const string CheckNodeIdHint = "Check the node id and its type.";

    /// <summary>Hint shown when a plugin-only tool times out waiting for the plugin.</summary>
    public const string PluginWindowHint = "Make sure the plugin window in Figma is open and not minimised.";

    /// <summary>Hint shown when a read tool fails on a bad argument or an unresolved file.</summary>
    public const string PassFileUrlHint = "Pass fileUrl explicitly.";

    /// <summary>Hint shown when a call lacks an argument the tool requires.</summary>
    public const string PassRequiredArgumentsHint = "Pass every required argument; the tool's input schema names them.";

    /// <summary>Hint shown when a command is refused because several files are connected and no target was chosen.</summary>
    public const string HowToChooseTarget =
        "Call figma_navigate with the link of the file you work in; figma_list_open_files shows the connected files. " +
        "The target then stays until you navigate elsewhere.";

    /// <summary>READ-channel hints: a read always has a REST fallback.</summary>
    public static class ReadFallback
    {
        /// <summary>Hint shown when a read tool finds no plugin, offering the REST source besides starting the plugin.</summary>
        public const string NotConnected =
            "Open the file in Figma Desktop and run the plugin, or retry with source=\"rest\".";

        /// <summary>Hint shown when a read command fails in the plugin, offering a node id check or the REST source.</summary>
        public const string CommandFailed = "Check the node id, or retry with source=\"rest\".";

        /// <summary>Hint shown when a read times out in the plugin, offering a check of the plugin window or the REST source.</summary>
        public const string Timeout =
            "Make sure the plugin window in Figma is open and not minimised, or retry with source=\"rest\".";
    }

    /// <summary>Error shown when a read tool receives a <c>source</c> value it does not recognise.</summary>
    public static string UnknownSource(string? source) => $"Unknown source '{source}'.";

    /// <summary>Hint shown with the unknown-source error, listing every <see cref="ReadSource"/> value the read tools accept.</summary>
    public static readonly string AllowedSources =
        $"Allowed: {string.Join(", ", Enum.GetNames<ReadSource>().Select(n => n.ToLowerInvariant()))}.";

    /// <summary>Error shown when a read asks for <c>source="plugin"</c> while no plugin runs in the file.</summary>
    public static string PluginUnavailable(string fileKey) => $"The plugin is not connected to file {fileKey}.";

    /// <summary>Hint shown with the plugin-unavailable error, telling how to start the plugin or read through REST instead.</summary>
    public const string PluginUnavailableHint =
        "Open the file in Figma Desktop and run the Snail Bridge plugin, or call with source=\"rest\".";
}
