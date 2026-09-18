using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Server self-check: config, plugin presence, cache availability.</summary>
[McpServerToolType]
public sealed class DiagnosticsTools(
    ServerConfig config,
    ISnapshotStoreHealth cacheDiagnostics,
    ILiveEditorLink bridge,
    IOpenDocuments sessions,
    IQuotaStatus rateLimit,
    IEditorExtensionInstaller installer,
    IApiIdentity identity)
{
    [McpServerTool(Name = "csharp_diagnose", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Diagnostics.Diagnose)]
    public async Task<string> DiagnoseAsync(
        [Description("Open the cache file and check that it reads.")]
        bool probeDatabase = false,
        [Description("Call GET /v1/me to prove the Figma token actually works; costs one cheap request.")]
        bool probeToken = false,
        CancellationToken cancellationToken = default)
    {
        var report = new JsonObject
        {
            ["server"] = new JsonObject
            {
                ["name"] = ThisAssembly.Name,
                ["version"] = ThisAssembly.InformationalVersion,
                ["mode"] = config.Mode.ToString(),
            },
            ["config"] = new JsonObject
            {
                ["configFile"] = ServerPaths.FindConfigFile(),
                ["dataDirectory"] = config.DataDirectory,
                ["figmaAccessToken"] = await DescribeTokenAsync(probeToken, cancellationToken).ConfigureAwait(false),
                ["sanitizationProfile"] = config.Sanitization.Profile.ToString(),
                ["metrics"] = config.Sanitization.LogMetrics,
            },
            ["bridge"] = new JsonObject
            {
                ["portRange"] = $"{config.Bridge.PortRangeStart}-{config.Bridge.PortRangeEnd}",
                ["path"] = config.Bridge.Path,
                ["requestTimeoutSeconds"] = config.Bridge.RequestTimeoutSeconds,
                ["preferPluginForReads"] = config.Bridge.PreferPluginForReads,
                ["listeningOn"] = bridge.Address,
                ["status"] = !bridge.IsListening
                    ? "not listening — the whole port range is taken"
                    : sessions.HasConnection ? "the plugin is connected" : "listening, the plugin is not connected",
                ["connectedFiles"] = sessions.Sessions.Count,
                ["activeFile"] = sessions.ActiveFileKey,
            },
            ["plugin"] = new JsonObject
            {
                ["bundledPath"] = installer.BundledDirectory,
                ["bundled"] = installer.BundledDirectory is not null,
                ["installPath"] = installer.TargetDirectory,
                ["installed"] = installer.IsInstalled,
            },
            ["cache"] = BuildCacheReport(probeDatabase),
            ["rateLimit"] = BuildRateLimitReport(),
        };

        return report.ToJsonString(SerializerOptions);
    }

    /// <summary>Without the probe only says whether the variable is set; with it, whether Figma accepts the token.</summary>
    private async Task<JsonNode> DescribeTokenAsync(bool probeToken, CancellationToken cancellationToken)
    {
        if (!config.HasAccessToken)
        {
            return "not set";
        }

        if (!probeToken)
        {
            return "set (pass probeToken=true to verify it against Figma)";
        }

        try
        {
            var me = await identity.GetMeAsync(cancellationToken).ConfigureAwait(false);

            return new JsonObject
            {
                ["status"] = "valid",
                ["handle"] = me["handle"]?.DeepClone(),
                ["email"] = me["email"]?.DeepClone(),
            };
        }
        catch (PublishedSourceException ex)
        {
            return new JsonObject
            {
                ["status"] = "REJECTED by Figma",
                ["error"] = ex.Message,
                ["hint"] = "Issue a new personal access token at figma.com → Settings → Security and put it into SNAIL_MCP_FIGMA_ACCESS_TOKEN.",
            };
        }
    }

    /// <summary>Figma buckets closed by exhausted quota — the first thing to check when a tool suddenly stops working.</summary>
    private JsonObject BuildRateLimitReport()
    {
        var blocked = new JsonArray();

        foreach (var (bucket, until) in rateLimit.ActiveBlocks.OrderBy(pair => pair.Value))
        {
            blocked.Add(new JsonObject
            {
                ["bucket"] = bucket,
                ["until"] = until.ToString("O"),
                ["remaining"] = PublishedSourceException.Describe(until - DateTimeOffset.UtcNow),
            });
        }

        return new JsonObject
        {
            ["blockedBuckets"] = blocked,
            ["note"] = blocked.Count == 0
                ? "the Figma quota is not blocked"
                : "while the bucket is closed no requests are sent to it — the picture can be taken through the plugin (figma_take_screenshot or figma_get_component_image)",
        };
    }

    private JsonObject BuildCacheReport(bool probeDatabase)
    {
        var databaseFile = config.CacheDatabaseFile;
        var cache = new JsonObject
        {
            ["enabled"] = config.Cache.Enabled,
            ["databaseFile"] = databaseFile,
            ["sizeBytes"] = File.Exists(databaseFile) ? new FileInfo(databaseFile).Length : null,
            ["ttlHours"] = config.Cache.TtlHours,
            ["imageTtlHours"] = config.Cache.ImageTtlHours,
            ["started"] = cacheDiagnostics.IsStarted,
            ["lastError"] = cacheDiagnostics.LastError,
        };

        if (!probeDatabase)
        {
            cache["probe"] = "skipped (probeDatabase=false)";

            return cache;
        }

        var probe = cacheDiagnostics.Probe();

        if (probe.Ok)
        {
            cache["probe"] = "ok";
            cache["started"] = true;
            cache["entries"] = probe.NodeEntries;
            cache["imageEntries"] = probe.ImageEntries;
        }
        else
        {
            cache["probe"] = "failed";
            cache["error"] = probe.Error;
        }

        return cache;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
