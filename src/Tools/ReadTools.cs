using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tools;

/// <summary>READ channel: file, components, variables and styles with cache and sanitization; plugin-first when a session exists.</summary>
[McpServerToolType]
public sealed class ReadTools(
    IPublishedDocumentSource api,
    DocumentReader reader,
    DocumentRenderer images,
    ILiveEditor plugin,
    ICaptureStore screenshots,
    ActiveDocumentTracker activeFile,
    NodeSanitizer sanitizer,
    SanitizationOptions sanitizationOptions,
    EditorLinkOptions bridgeOptions,
    ILogger<ReadTools> logger)
{
    /// <summary>A live plugin session exists for the file — reading through it is free and fresher than REST.</summary>
    private bool HasLiveSession(string fileKey) => plugin.Documents.Resolve(fileKey) is not null;

    /// <summary>In auto mode the plugin goes first unless disabled in configuration.</summary>
    private bool PluginPreferred => bridgeOptions.PreferPluginForReads;

    /// <summary>Read mode as a lowercase string — exactly how it is compared downstream.</summary>
    private static bool TryParseSource(string? source, out string mode)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            mode = "auto";

            return true;
        }

        if (Enum.TryParse<ReadSource>(source.Trim(), ignoreCase: true, out var parsed))
        {
            mode = parsed.ToString().ToLowerInvariant();

            return true;
        }

        mode = source.Trim().ToLowerInvariant();

        return false;
    }

    private static string UnknownSource(string? source) =>
        ToolResponse.Error(Messages.UnknownSource(source), Messages.AllowedSources);

    private static string PluginUnavailable(string fileKey) =>
        ToolResponse.Error(Messages.PluginUnavailable(fileKey), Messages.PluginUnavailableHint);

    [McpServerTool(Name = "figma_get_file_data", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetFileData)]
    public Task<string> GetFileDataAsync(
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description("Tree traversal depth. 1 is pages only, 2 is top-level frames.")]
        int? depth = null,
        [Description("Cap on the number of nodes in the response.")]
        int? maxNodes = null,
        [Description("Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only.")]
        string? profile = null,
        [Description(ToolDescriptions.Parameters.Source)]
        string source = "auto",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseSource(source, out var mode))
            {
                return UnknownSource(source);
            }

            var fileKey = activeFile.Resolve(fileUrl);
            var effectiveDepth = depth ?? sanitizationOptions.DefaultDepth;
            var effectiveMaxNodes = maxNodes ?? sanitizationOptions.DefaultMaxNodes;

            if (mode != "rest" && (mode == "plugin" || PluginPreferred) && HasLiveSession(fileKey))
            {
                try
                {
                    var payload = await plugin.ReadTreeAsync(fileKey, null, effectiveDepth, effectiveMaxNodes, ct)
                        .ConfigureAwait(false);

                    if (payload?["node"] is { } node)
                    {
                        return ToolResponse.Ok(node.DeepClone(), envelope =>
                        {
                            envelope["fileKey"] = fileKey;
                            envelope["source"] = "plugin";
                            envelope["nodeCount"] = payload["nodeCount"]?.DeepClone();
                        });
                    }
                }
                catch (Exception ex) when (
                    mode == "auto" && ex is EditorUnavailableException or EditorCommandException or TimeoutException)
                {
                    logger.LogWarning("Plugin did not return the file tree, falling back to REST: {Message}", ex.Message);
                }
            }
            else if (mode == "plugin")
            {
                return PluginUnavailable(fileKey);
            }

            var request = BuildRequest(profile, depth, maxNodes);

            var read = await reader.ReadFileAsync(
                fileKey,
                new DocumentReadOptions { Depth = effectiveDepth },
                request,
                ct).ConfigureAwait(false);

            return ToolResponse.FromRead(read, fileKey);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_component", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetComponent)]
    public Task<string> GetComponentAsync(
        [Description("Node id, for example '1:234'. The dashed form from a link is accepted too.")]
        string nodeId,
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description("Traversal depth of the node subtree. The default 4 is enough to see what a component is made of.")]
        int? depth = null,
        [Description("Cleaning profile: aggressive (the default), balanced, raw. It affects the REST path only.")]
        string? profile = null,
        [Description(ToolDescriptions.Parameters.Source)]
        string source = "auto",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseSource(source, out var mode))
            {
                return UnknownSource(source);
            }

            var fileKey = activeFile.Resolve(fileUrl);
            var normalized = NodeId.Normalize(nodeId);
            var effectiveDepth = depth ?? 4;

            if (mode != "rest" && (mode == "plugin" || PluginPreferred) && HasLiveSession(fileKey))
            {
                try
                {
                    var payload = await plugin.ReadTreeAsync(
                        fileKey, normalized, effectiveDepth, sanitizationOptions.DefaultMaxNodes, ct).ConfigureAwait(false);

                    if (payload?["found"]?.GetValue<bool>() == false)
                    {
                        return ToolResponse.Error(
                            $"Node {normalized} was not found in the file.",
                            "Check the id. The last saved state can be asked for with source=\"rest\".");
                    }

                    if (payload?["node"] is { } node)
                    {
                        return ToolResponse.Ok(node.DeepClone(), envelope =>
                        {
                            envelope["fileKey"] = fileKey;
                            envelope["nodeId"] = normalized;
                            envelope["source"] = "plugin";
                        });
                    }
                }
                catch (Exception ex) when (
                    mode == "auto" && ex is EditorUnavailableException or EditorCommandException or TimeoutException)
                {
                    logger.LogWarning("Plugin did not return node {NodeId}, falling back to REST: {Message}", normalized, ex.Message);
                }
            }
            else if (mode == "plugin")
            {
                return PluginUnavailable(fileKey);
            }

            var request = BuildRequest(profile, effectiveDepth, maxNodes: null);

            var read = await reader.ReadNodeAsync(
                fileKey,
                nodeId,
                new NodeReadOptions { Depth = effectiveDepth },
                request,
                ct).ConfigureAwait(false);

            return ToolResponse.FromRead(read, fileKey, envelope => envelope["nodeId"] = normalized);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_variables", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetVariables)]
    public Task<string> GetVariablesAsync(
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description("Add the published variables of the library, not just the local ones.")]
        bool includePublished = false,
        [Description("Format: 'summary' (the default, the summary only), 'full' (every variable).")]
        string format = "summary",
        [Description("Keep only the variables whose name contains the substring.")]
        string? namePattern = null,
        [Description($"{ToolDescriptions.Parameters.Source} The plugin works on any plan, Variables REST is Enterprise-only.")]
        string source = "auto",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseSource(source, out var mode))
            {
                return UnknownSource(source);
            }

            var fileKey = activeFile.Resolve(fileUrl);

            JsonNode? local = null;
            var localSource = "figma-api";

            if (mode != "rest" && (mode == "plugin" || PluginPreferred) && HasLiveSession(fileKey))
            {
                try
                {
                    local = await plugin.ReadAsync(
                        EditorCommands.GetVariablesData,
                        fileKey: fileKey,
                        timeout: TimeSpan.FromSeconds(45),
                        cancellationToken: ct).ConfigureAwait(false);
                    localSource = "plugin";
                }
                catch (Exception ex) when (
                    mode == "auto" && ex is EditorUnavailableException or EditorCommandException or TimeoutException)
                {
                    logger.LogWarning("Plugin did not return variables, falling back to REST: {Message}", ex.Message);
                }
            }
            else if (mode == "plugin")
            {
                return PluginUnavailable(fileKey);
            }

            local ??= await api.GetLocalVariablesAsync(fileKey, ct).ConfigureAwait(false);
            var formatted = VariableFormatter.Format(local, namePattern);

            if (includePublished)
            {
                try
                {
                    var published = await api.GetPublishedVariablesAsync(fileKey, ct).ConfigureAwait(false);
                    formatted["published"] = VariableFormatter.Format(published, namePattern)["variables"]?.DeepClone();
                }
                catch (PublishedSourceException ex)
                {
                    logger.LogWarning("Published variables unavailable: {Message}", ex.Message);
                    formatted["publishedError"] = ex.Message;
                }
            }

            if (!string.Equals(format, "full", StringComparison.OrdinalIgnoreCase))
            {
                formatted.Remove("variables");
                formatted.Remove("published");
            }

            return ToolResponse.Ok(formatted, envelope =>
            {
                envelope["fileKey"] = fileKey;
                envelope["source"] = localSource;
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_file_meta", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetFileMeta)]
    public Task<string> GetFileMetaAsync(
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var response = await api.GetFileMetaAsync(fileKey, ct).ConfigureAwait(false);
            var meta = response["file"] ?? response["meta"] ?? response;

            return ToolResponse.Ok(new JsonObject
            {
                ["fileKey"] = fileKey,
                ["name"] = meta?["name"]?.DeepClone(),
                ["lastTouchedAt"] = meta?["last_touched_at"]?.DeepClone() ?? meta?["last_modified"]?.DeepClone(),
                ["lastTouchedBy"] = meta?["last_touched_by"]?["handle"]?.DeepClone(),
                ["editorType"] = meta?["editor_type"]?.DeepClone(),
                ["version"] = meta?["version"]?.DeepClone(),
                ["folderName"] = meta?["folder_name"]?.DeepClone(),
                ["creator"] = meta?["creator"]?["handle"]?.DeepClone(),
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_styles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetStyles)]
    public Task<string> GetStylesAsync(
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description(ToolDescriptions.Parameters.Source)]
        string source = "auto",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseSource(source, out var mode))
            {
                return UnknownSource(source);
            }

            var fileKey = activeFile.Resolve(fileUrl);
            JsonNode? styles = null;
            var stylesSource = "figma-api";

            if (mode != "rest" && (mode == "plugin" || PluginPreferred) && HasLiveSession(fileKey))
            {
                try
                {
                    var payload = await plugin.ReadStylesAsync(fileKey, ct).ConfigureAwait(false);
                    styles = payload?["styles"];
                    stylesSource = "plugin";
                }
                catch (Exception ex) when (
                    mode == "auto" && ex is EditorUnavailableException or EditorCommandException or TimeoutException)
                {
                    logger.LogWarning("Plugin did not return styles, falling back to REST: {Message}", ex.Message);
                }
            }
            else if (mode == "plugin")
            {
                return PluginUnavailable(fileKey);
            }

            if (styles is null)
            {
                var response = await api.GetStylesAsync(fileKey, ct).ConfigureAwait(false);
                styles = response["meta"]?["styles"] ?? new JsonArray();
                stylesSource = "figma-api";
            }

            var byType = new JsonObject();

            if (styles is JsonArray array)
            {
                foreach (var group in array.GroupBy(item => item?["style_type"]?.GetValue<string>() ?? "UNKNOWN"))
                {
                    byType[group.Key] = group.Count();
                }
            }

            return ToolResponse.Ok(
                new JsonObject { ["styles"] = styles.DeepClone(), ["countByType"] = byType },
                envelope =>
                {
                    envelope["fileKey"] = fileKey;
                    envelope["source"] = stylesSource;
                });
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_component_image", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetComponentImage)]
    public Task<CallToolResult> GetComponentImageAsync(
        [Description("Node id, for example '1:234'.")]
        string nodeId,
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description("Format: png (the default), jpg, svg, pdf.")]
        string format = "png",
        [Description("Render scale, from 0.01 to 4.")]
        double scale = 2,
        [Description("Source: auto (the default — the cache, then the plugin, then REST), rest (Figma REST only, spends quota), plugin (the plugin only).")]
        string source = "auto",
        [Description(ToolDescriptions.Parameters.Deliver)]
        string deliver = PictureDelivery.PathOnly,
        CancellationToken cancellationToken = default) =>
        ExecutePicturedAsync(async ct =>
        {
            if (!PictureDelivery.TryParse(deliver, out var inline, out var deliverError))
            {
                return ToolResponse.Textual(ToolResponse.Error(deliverError!, PictureDelivery.Hint));
            }

            if (!TryParseSource(source, out var mode))
            {
                return ToolResponse.Textual(UnknownSource(source));
            }

            var fileKey = activeFile.Resolve(fileUrl);
            var normalized = NodeId.Normalize(nodeId);
            var effectiveScale = Math.Clamp(scale, 0.01, 4);

            if (mode != "rest" && (mode == "plugin" || PluginPreferred) && HasLiveSession(fileKey))
            {
                var captured = await CaptureThroughPluginAsync(fileKey, normalized, format, effectiveScale, inline, ct)
                    .ConfigureAwait(false);

                if (captured is { } shot)
                {
                    return shot;
                }

                if (mode == "plugin")
                {
                    return ToolResponse.Textual(ToolResponse.Error(
                        $"The plugin returned no picture for node {normalized}.",
                        "Check the node id or retry with source=\"rest\"."));
                }
            }
            else if (mode == "plugin")
            {
                return ToolResponse.Textual(PluginUnavailable(fileKey));
            }

            if (mode != "plugin")
            {
                var cachedUrl = await images
                    .TryGetCachedAsync(fileKey, normalized, format, effectiveScale, ct)
                    .ConfigureAwait(false);

                if (cachedUrl is not null)
                {
                    return ToolResponse.Textual(ImageResponse(fileKey, normalized, format, effectiveScale, "cache",
                        data => data["url"] = cachedUrl));
                }
            }

            try
            {
                var render = await images
                    .RenderAsync(fileKey, [normalized], format, effectiveScale, ct)
                    .ConfigureAwait(false);

                return ToolResponse.Textual(render.Urls.TryGetValue(normalized, out var url)
                    ? ImageResponse(fileKey, normalized, format, effectiveScale, "figma-api",
                        data => data["url"] = url)
                    : ToolResponse.Error(
                        $"Figma did not render node {normalized}.",
                        "Check that the node exists and that it is a visual node, not a page."));
            }
            catch (PublishedSourceException ex) when (ex.Failure == SourceFailure.RateLimited)
            {
                var captured = HasLiveSession(fileKey)
                    ? await CaptureThroughPluginAsync(fileKey, normalized, format, effectiveScale, inline, ct).ConfigureAwait(false)
                    : null;

                if (captured is { } shot)
                {
                    logger.LogWarning("Render quota exhausted — node {NodeId} captured through the plugin", normalized);

                    return shot;
                }

                return ToolResponse.Textual(ToolResponse.Error(
                    ex.Message,
                    $"{ex.Hint} The picture can be taken without quota: open the file in Figma Desktop, run the Snail Bridge plugin and repeat the call (or capture the whole page with figma_take_screenshot)."));
            }
        }, cancellationToken);

    /// <summary>Node capture through the plugin, or <c>null</c> when it could not deliver.</summary>
    /// <remarks>A null lets the caller decide between failing and spending REST quota. The picture is attached here rather than by the caller, because the capture store takes the base64 out of the payload on its way to disk.</remarks>
    private async Task<CallToolResult?> CaptureThroughPluginAsync(
        string fileKey,
        string nodeId,
        string format,
        double scale,
        bool inline,
        CancellationToken cancellationToken)
    {
        if (format.ToLowerInvariant() is not ("png" or "jpg"))
        {
            return null;
        }

        try
        {
            var result = await plugin.ReadAsync(
                EditorCommands.CaptureScreenshot,
                new JsonObject
                {
                    ["nodeId"] = nodeId,
                    ["format"] = format.ToUpperInvariant(),
                    ["scale"] = scale,
                },
                fileKey: fileKey,
                timeout: TimeSpan.FromSeconds(60),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result?["image"] is not JsonObject image)
            {
                return null;
            }

            var picture = PictureDelivery.From(image, inline);
            var saved = screenshots.SaveAndReplace(image);

            return ToolResponse.Pictured(
                ImageResponse(fileKey, nodeId, format, scale, "plugin", data => data["image"] = saved.DeepClone()),
                picture);
        }
        catch (Exception ex) when (ex is EditorUnavailableException or EditorCommandException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning("Plugin could not capture node {NodeId}: {Message}", nodeId, ex.Message);

            return null;
        }
    }

    /// <summary>Uniform render response: the source in the envelope, the payload in data.</summary>
    private static string ImageResponse(
        string fileKey,
        string nodeId,
        string format,
        double scale,
        string source,
        Action<JsonObject> fill)
    {
        var data = new JsonObject
        {
            ["nodeId"] = nodeId,
            ["format"] = format,
            ["scale"] = scale,
        };

        fill(data);

        return ToolResponse.Ok(data, envelope =>
        {
            envelope["fileKey"] = fileKey;
            envelope["source"] = source;
        });
    }

    [McpServerTool(Name = "figma_get_file_for_plugin", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Read.GetFileForPlugin)]
    public Task<string> GetFileForPluginAsync(
        [Description("Link to the Figma file. Optional if the file has already been used in this session.")]
        string? fileUrl = null,
        [Description("Tree traversal depth, at most 5.")]
        int depth = 3,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);
            var effectiveDepth = Math.Clamp(depth, 1, ToolLimits.MaxPluginViewDepth);

            var raw = await api.GetDocumentAsync(fileKey, new DocumentReadOptions { Depth = effectiveDepth }, ct)
                .ConfigureAwait(false);

            var projected = StructureProjector.Project(raw["document"] ?? raw);
            var sanitized = sanitizer.Sanitize(
                projected,
                new SanitizationRequest
                {
                    Profile = SanitizationProfile.Aggressive,
                    MaxDepth = effectiveDepth,
                    DeduplicateStyles = false,
                },
                $"{fileKey}/plugin-view");

            return ToolResponse.Ok(sanitized.Node, envelope =>
            {
                envelope["fileKey"] = fileKey;
                envelope["metrics"] = new JsonObject
                {
                    ["bytesIn"] = sanitized.BytesIn,
                    ["bytesOut"] = sanitized.BytesOut,
                    ["savedRatio"] = Math.Round(sanitized.Ratio, 4),
                };
            });
        });

    /// <summary>Profile and limits of the request on top of configured defaults.</summary>
    private SanitizationRequest BuildRequest(string? profile, int? depth, int? maxNodes)
    {
        var parsed = sanitizationOptions.Profile;

        if (!string.IsNullOrWhiteSpace(profile) &&
            Enum.TryParse<SanitizationProfile>(profile.Trim(), ignoreCase: true, out var explicitProfile))
        {
            parsed = explicitProfile;
        }

        return new SanitizationRequest
        {
            Profile = parsed,
            MaxDepth = depth ?? sanitizationOptions.DefaultDepth,
            MaxNodes = maxNodes ?? sanitizationOptions.DefaultMaxNodes,
        };
    }

    /// <summary>Errors come back as data so the model can read the cause and fix the call.</summary>
    private Task<string> ExecuteAsync(Func<CancellationToken, Task<string>> action, CancellationToken cancellationToken = default) =>
        GuardAsync(action, static (message, hint) => ToolResponse.Error(message, hint), cancellationToken);

    /// <summary>The same guard for the tool that may answer with a picture.</summary>
    private Task<CallToolResult> ExecutePicturedAsync(
        Func<CancellationToken, Task<CallToolResult>> action,
        CancellationToken cancellationToken = default) =>
        GuardAsync(action, static (message, hint) => ToolResponse.Textual(ToolResponse.Error(message, hint)), cancellationToken);

    /// <summary>One error ladder for both result shapes: what differs is only how the message and the hint are packed.</summary>
    private async Task<T> GuardAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Func<string, string?, T> fail,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (PublishedSourceException ex)
        {
            logger.LogWarning(ex, "Figma API returned an error");

            return fail(ex.Message, ex.Hint);
        }
        catch (EditorUnavailableException ex)
        {
            return fail(ex.Message, Messages.ReadFallback.NotConnected);
        }
        catch (EditorCommandException ex)
        {
            return fail(ex.Message, Messages.ReadFallback.CommandFailed);
        }
        catch (TimeoutException ex)
        {
            return fail(ex.Message, Messages.ReadFallback.Timeout);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return fail(ex.Message, Messages.PassFileUrlHint);
        }
    }
}
