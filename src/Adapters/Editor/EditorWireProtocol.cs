namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Wire contract with the editor extension: request envelopes and event names, implemented by <c>plugin/ui.html</c>.</summary>
public static class EditorWireProtocol
{
    /// <summary>Server request: <c>{ id, method, params }</c>.</summary>
    public static JsonObject Request(string id, string method, JsonObject? parameters) => new()
    {
        ["id"] = id,
        ["method"] = method,
        ["params"] = parameters ?? new JsonObject(),
    };

    /// <summary>Greeting sent on connect; the plugin writes it to its log.</summary>
    public static JsonObject ServerHello(int port, string version) => new()
    {
        ["type"] = "SERVER_HELLO",
        ["data"] = new JsonObject
        {
            ["port"] = port,
            ["pid"] = Environment.ProcessId,
            ["serverVersion"] = version,
            ["startedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        },
    };

    /// <summary>Told to the session whose running plugin is older than the bundle installed on disk; ui.html shows the update banner.</summary>
    public static JsonObject PluginUpdateAvailable() => new()
    {
        ["type"] = "PLUGIN_UPDATE_AVAILABLE",
    };

    /// <summary>Live sibling servers found by the server-side range scan; ui.html dials the ports it is not connected to yet, so the browser never probes closed ports while connected.</summary>
    public static JsonObject PortsAlive(IReadOnlyList<int> ports) => new()
    {
        ["type"] = "PORTS_ALIVE",
        ["data"] = new JsonObject
        {
            ["ports"] = new JsonArray([.. ports.Select(port => (JsonNode)port)]),
        },
    };

    /// <summary>Unsolicited plugin messages.</summary>
    public static class Events
    {
        /// <summary>The plugin identified itself with fileKey and file name.</summary>
        public const string FileInfo = "FILE_INFO";

        /// <summary>The document changed — snapshots must be invalidated.</summary>
        public const string DocumentChange = "DOCUMENT_CHANGE";

        /// <summary>Selection changed; also makes the file active.</summary>
        public const string SelectionChange = "SELECTION_CHANGE";

        /// <summary>The user switched to another page.</summary>
        public const string PageChange = "PAGE_CHANGE";

        /// <summary>A console line from the plugin sandbox.</summary>
        public const string ConsoleCapture = "CONSOLE_CAPTURE";

        /// <summary>Node description or annotations changed.</summary>
        public const string MetadataChange = "METADATA_CHANGE";

        /// <summary>Full variables snapshot, pushed by the plugin on connect and after refreshes.</summary>
        public const string VariablesData = "VARIABLES_DATA";
    }
}
