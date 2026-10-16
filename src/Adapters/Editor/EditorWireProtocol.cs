namespace Snail.MCP.Figma.Adapters.Editor;

/// <summary>Wire contract with the editor extension: request envelopes and event names, implemented by <c>plugin/ui.html</c>.</summary>
public static class EditorWireProtocol
{
    /// <summary>A plugin message parsed from the bytes it arrived in; <c>null</c> for the JSON literal <c>null</c>.</summary>
    /// <remarks>The bytes used to be copied out of the stream, decoded into a string twice their size and parsed from it: a 5.6 MB screenshot reply allocated 21.3 MB. Parsed from the buffer in place it allocates 5.3 MB and takes 1.3 ms instead of 18.5 ms (BridgeBenchmarks, 2026-10-16).</remarks>
    public static JsonNode? Parse(MemoryStream received) =>
        JsonNode.Parse(received.GetBuffer().AsSpan(0, (int)received.Length));

    /// <summary>The protocol in which one connection carries several agents and every message about an agent names it.</summary>
    /// <remarks>A panel of protocol 1 knows one agent per connection, which is what a server per agent gave it. Both sides say which one they speak: the server in <c>SERVER_HELLO</c>, the panel in <c>FILE_INFO</c>, and each answers the other in the older one until told otherwise.</remarks>
    public const int AgentsProtocol = 2;

    /// <summary>Server request: <c>{ id, method, params, agent }</c>; the agent lets the panel answer remarks to whoever commanded last.</summary>
    public static JsonObject Request(string id, string method, JsonObject? parameters, string? agent = null)
    {
        var request = new JsonObject
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? [],
        };

        if (agent is not null)
        {
            request["agent"] = agent;
        }

        return request;
    }

    /// <summary>Greeting sent on connect; the plugin writes it to its log and keeps the agent's name for the panel.</summary>
    /// <remarks>Two agents in one file are told apart by name: without it the human would choose between port numbers.</remarks>
    public static JsonObject ServerHello(int port, string version, string agent) => new()
    {
        ["type"] = "SERVER_HELLO",
        ["data"] = new JsonObject
        {
            ["port"] = port,
            ["pid"] = Environment.ProcessId,
            ["serverVersion"] = version,
            ["agent"] = agent,
            ["protocol"] = AgentsProtocol,
            ["startedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        },
    };

    /// <summary>Whether this server works in the panel's file; the panel sends remarks only to servers that said yes.</summary>
    /// <remarks>The panel is connected to every server, and before this it addressed the one that last sent a command — which is nobody before the first command, and the wrong one once the agent moved on.</remarks>
    public static JsonObject TargetBound(bool bound) => new()
    {
        ["type"] = "TARGET_BOUND",
        ["data"] = new JsonObject
        {
            ["bound"] = bound,
        },
    };

    /// <summary>Every agent of this server and how it stands in the panel's file: whether it works there and how many remarks wait for it.</summary>
    /// <remarks>Protocol 2 only. It replaces <c>TARGET_BOUND</c> and <c>REMARKS_WAITING</c>, which can speak for one agent per connection, and it is sent whole on every change, so the panel never has to merge.</remarks>
    public static JsonObject Agents(JsonArray agents) => new()
    {
        ["type"] = "AGENTS",
        ["data"] = new JsonObject
        {
            ["agents"] = agents,
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

    /// <summary>How many remarks are still waiting to be read; the panel keeps its own wave burning until this says zero.</summary>
    /// <remarks>The queue lives on the server, so the panel cannot see a remark being taken and would light a wave that never goes out.</remarks>
    public static JsonObject RemarksWaiting(int waiting) => new()
    {
        ["type"] = "REMARKS_WAITING",
        ["data"] = new JsonObject
        {
            ["waiting"] = waiting,
        },
    };

    /// <summary>How far a stepped build has got, so the panel can show a share instead of a wave.</summary>
    /// <remarks>Only the server knows the total: the plugin sees each step as a separate command and cannot tell the second of ten from the last. A step of zero means the build is over and the panel goes quiet.</remarks>
    public static JsonObject BuildProgress(int step, int total) => new()
    {
        ["type"] = "BUILD_PROGRESS",
        ["data"] = new JsonObject
        {
            ["step"] = step,
            ["total"] = total,
        },
    };

    /// <summary>Unsolicited plugin messages.</summary>
    public static class Events
    {
        /// <summary>The plugin identified itself with fileKey and file name.</summary>
        public const string FileInfo = "FILE_INFO";

        /// <summary>The document changed — snapshots must be invalidated.</summary>
        public const string DocumentChange = "DOCUMENT_CHANGE";

        /// <summary>Selection changed; stamps the human's interaction on the session and moves no target.</summary>
        public const string SelectionChange = "SELECTION_CHANGE";

        /// <summary>The user switched to another page.</summary>
        public const string PageChange = "PAGE_CHANGE";

        /// <summary>A console line from the plugin sandbox.</summary>
        public const string ConsoleCapture = "CONSOLE_CAPTURE";

        /// <summary>Node description or annotations changed.</summary>
        public const string MetadataChange = "METADATA_CHANGE";

        /// <summary>Full variables snapshot, pushed by the plugin on connect and after refreshes.</summary>
        public const string VariablesData = "VARIABLES_DATA";

        /// <summary>The human stepped in from the plugin panel: a running build ends after the current step, and the optional data carries the remark, what was selected as it was written, and in protocol 2 the <c>agent</c> it is for.</summary>
        public const string BuildStop = "BUILD_STOP";

        /// <summary>The human took the waiting remarks back before anyone read them; in protocol 2 the data names the <c>agent</c>, and without it every agent's remarks go.</summary>
        public const string RemarksClear = "REMARKS_CLEAR";

        /// <summary>The panel names the agents working in its file; the data carries them as <c>agents</c>.</summary>
        public const string Peers = "PEERS";
    }
}
