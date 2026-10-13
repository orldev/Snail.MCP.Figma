using System.Diagnostics.CodeAnalysis;

namespace Snail.MCP.Figma.Configuration;

/// <summary>Server mode; the donor's Cloudflare mode is not ported.</summary>
public enum ServerMode
{
    Local,
}

/// <summary>Sanitization profile applied to Figma nodes before they reach the LLM.</summary>
public enum SanitizationProfile
{
    /// <summary>Maximum savings: geometry, defaults and empty arrays are stripped.</summary>
    Aggressive,

    /// <summary>Strips only the clearly useless: vector geometry and render bounds.</summary>
    Balanced,

    /// <summary>Strips nothing — the raw Figma API response.</summary>
    Raw,
}

/// <summary>How clients reach the server.</summary>
public enum Transport
{
    /// <summary>One process per client, started by it, over stdin and stdout.</summary>
    Stdio,

    /// <summary>One process for every client on this machine, kept by launchd, over Streamable HTTP on loopback.</summary>
    Http,
}

/// <summary>Root server configuration, bound from a JSON file and environment variables.</summary>
public sealed class ServerConfig
{
    public const string SectionName = "Snail";

    /// <summary>Figma personal or OAuth token; set it through <c>SNAIL_MCP_FIGMA_ACCESS_TOKEN</c>, never in a file.</summary>
    public string? AccessToken { get; set; }

    public ServerMode Mode { get; set; } = ServerMode.Local;

    /// <summary>The address of the MCP endpoint when <see cref="Transport"/> is <see cref="Transport.Http"/>; outside the bridge's port range.</summary>
    public const string DefaultUrl = "http://127.0.0.1:9233";

    /// <summary>stdio by default; <c>SNAIL_MCP_FIGMA_TRANSPORT=Http</c> makes this the one server every agent on the machine shares.</summary>
    public Transport Transport { get; set; } = Transport.Stdio;

    /// <summary>Where the shared server listens for MCP; <see cref="DefaultUrl"/> when not set.</summary>
    public string? Url { get; set; }

    public bool IsShared => Transport is Transport.Http;

    /// <summary>Data root: database, installed plugin, logs; defaults to <c>~/.s3l-mcp-figma</c>.</summary>
    public string DataDirectory { get; set; } = ServerPaths.DefaultDataDirectory;

    public EditorLinkOptions Bridge { get; set; } = new();

    public CacheOptions Cache { get; set; } = new();

    public SanitizationOptions Sanitization { get; set; } = new();

    public ScreenshotOptions Screenshots { get; set; } = new();

    /// <summary>Minutes without a tool call after which the process exits, unless a plugin is connected; 0 disables the watchdog.</summary>
    public int IdleTimeoutMinutes { get; set; } = 60;

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>Cache database file (SQLite).</summary>
    public string CacheDatabaseFile => Path.Combine(DataDirectory, Cache.DatabaseFileName);

    /// <summary>Legacy embedded RavenDB directory, kept only to remove it.</summary>
    public string LegacyRavenDirectory => Path.Combine(DataDirectory, "db");

    /// <summary>Directory where <c>EditorExtensionInstaller</c> installs the plugin for import into Figma.</summary>
    public string PluginDirectory => Path.Combine(DataDirectory, "plugin");

    /// <summary>Directory where <c>CaptureStore</c> saves figma_take_screenshot output.</summary>
    public string ScreenshotDirectory => Path.Combine(DataDirectory, "screenshots");
}

/// <summary>Settings of the WebSocket bridge to the Figma Desktop plugin.</summary>
public sealed class EditorLinkOptions
{
    /// <summary>Start of the port range; matches the plugin's <c>manifest.json</c> and cannot change without it.</summary>
    public int PortRangeStart { get; set; } = 9223;

    /// <summary>End of the port range, inclusive.</summary>
    public int PortRangeEnd { get; set; } = 9232;

    /// <summary>WebSocket endpoint path; the root by default because the vendored plugin connects to <c>ws://localhost:&lt;port&gt;</c> without a path.</summary>
    public string Path { get; set; } = "/";

    /// <summary>Timeout for one plugin reply.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Read through the plugin while it is connected: no quota and unsaved edits visible; REST stays the fallback and the only path for history, comments, libraries and svg/pdf.</summary>
    [SuppressMessage("Snail", "S3L0008", Justification = "A settings key: SNAIL_MCP_FIGMA_BRIDGE__PREFER_PLUGIN_FOR_READS, published in server.json, and config files bind by this name.")]
    public bool PreferPluginForReads { get; set; } = true;

    /// <summary>How the panel names this server when several agents share a file.</summary>
    /// <remarks>The MCP host starts the server in the agent's project folder, so that folder's name is the agent's name; <c>SNAIL_MCP_FIGMA_BRIDGE__AGENT_NAME</c> overrides it.</remarks>
    public string AgentName { get; set; } = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory));
}

/// <summary>Settings of the snapshot cache, a SQLite file every server process of the machine shares.</summary>
public sealed class CacheOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Database file name inside <see cref="ServerConfig.DataDirectory"/>.</summary>
    public string DatabaseFileName { get; set; } = "snapshots.sqlite";

    /// <summary>Safety TTL of a document; real invalidation comes from the file version and DOCUMENT_CHANGE.</summary>
    public int TtlHours { get; set; } = 24;

    /// <summary>How often expired documents are swept.</summary>
    public int ExpirationSweepSeconds { get; set; } = 60;

    /// <summary>Lifetime in hours of a cached render link; separate from <see cref="TtlHours"/> because renders sit in the most expensive bucket.</summary>
    public int ImageTtlHours { get; set; } = 24;

    /// <summary>Seconds a cached snapshot counts as fresh; the only throttle on expensive requests. Zero means ask Figma on every read.</summary>
    public int FreshnessSeconds { get; set; } = 300;

    /// <summary>Past-version snapshots kept in process memory; past versions never go stale and diff tools re-read them dozens of times.</summary>
    public int VersionSnapshotEntries { get; set; } = 50;
}

/// <summary>Sanitizer and savings-metric settings.</summary>
public sealed class SanitizationOptions
{
    public SanitizationProfile Profile { get; set; } = SanitizationProfile.Aggressive;

    /// <summary>Log <c>bytes_in / bytes_out / ratio</c> to stderr.</summary>
    [SuppressMessage("Snail", "S3L0008", Justification = "A settings key: SNAIL_MCP_FIGMA_SANITIZATION__LOG_METRICS and config files bind by this name.")]
    public bool LogMetrics { get; set; } = true;

    /// <summary>Default tree depth for progressive disclosure.</summary>
    public int DefaultDepth { get; set; } = 3;

    /// <summary>Node count cap per response.</summary>
    public int DefaultMaxNodes { get; set; } = 500;
}

/// <summary>Screenshot store settings; figma_take_screenshot returns a file on disk because base64 easily exceeds the result size limit.</summary>
public sealed class ScreenshotOptions
{
    /// <summary>How many recent files to keep in <see cref="ServerConfig.ScreenshotDirectory"/>.</summary>
    public int RetainCount { get; set; } = 30;
}
