using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;
using Snail.MCP.Figma.Application.Documents;

namespace Snail.MCP.Figma.Tools;

/// <summary>Session and console: open files, targeting, plugin output and canvas captures.</summary>
[McpServerToolType]
public sealed class SessionTools(
    ILiveEditor plugin,
    ActiveDocumentTracker activeFile,
    ICaptureStore screenshots,
    ILogger<SessionTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_list_open_files")]
    [Description(ToolDescriptions.Session.ListOpenFiles)]
    public Task<string> ListOpenFilesAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ => Task.FromResult(ToolResponse.Ok(new JsonObject
        {
            ["files"] = new JsonArray(Plugin.Documents.Sessions.Select(s => (JsonNode)s.Describe()).ToArray()),
            ["activeFileKey"] = Plugin.Documents.ActiveFileKey,
            ["targetLocked"] = Plugin.Documents.IsLocked,
        })), cancellationToken);

    [McpServerTool(Name = "figma_navigate")]
    [Description(ToolDescriptions.Session.Navigate)]
    public Task<string> NavigateAsync(
        [Description("Link to the Figma file or its key.")] string fileUrlOrKey,
        [Description("Pin the target to the agent.")] bool lockTarget = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            var fileKey = DocumentLocator.Parse(fileUrlOrKey)?.EffectiveKey ?? fileUrlOrKey.Trim();
            activeFile.Remember(fileKey);

            var switched = Plugin.Documents.SetActiveFile(fileKey, lockTarget);

            return Task.FromResult(ToolResponse.Ok(new JsonObject
            {
                ["fileKey"] = fileKey,
                ["pluginConnected"] = switched,
                ["targetLocked"] = Plugin.Documents.IsLocked,
                ["note"] = switched
                    ? "The file is selected, both reading and writing are available."
                    : "The plugin is not running in this file: reading through REST works, writing does not.",
            }));
        }, cancellationToken);

    [McpServerTool(Name = "figma_execute_across_files")]
    [Description(ToolDescriptions.Session.ExecuteAcrossFiles)]
    public Task<string> ExecuteAcrossFilesAsync(
        [Description("JavaScript code, the same as in figma_execute. Example: return figma.root.children.length;")]
        string code,
        [Description("Comma-separated keys of the target files. They come from figma_list_open_files.")] string? fileKeys = null,
        [Description("Run it over EVERY connected file. Requires explicit consent: the code will reach the pinned file too.")]
        bool allFiles = false,
        [Description("Per-file timeout in milliseconds, at most 30000. A slow file does not hold the others back.")]
        int timeoutMs = 10000,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var sessions = Plugin.Documents.Sessions.Where(session => session.IsOpen).ToList();

            if (sessions.Count == 0)
            {
                return NotConnected();
            }

            var requested = SplitFileKeys(fileKeys);

            if (requested.Count == 0 && !allFiles)
            {
                return ToolResponse.Error(
                    "No target files were given.",
                    "Pass comma-separated fileKeys or allFiles=true. figma_list_open_files provides the list of keys; " +
                    "for code that writes it is better to name the files explicitly.");
            }

            var addressable = sessions.Where(session => !string.IsNullOrEmpty(session.FileKey)).ToList();
            var unidentified = sessions
                .Where(session => string.IsNullOrEmpty(session.FileKey))
                .Select(session => session.FileName ?? session.ConnectionId)
                .ToList();

            var targets = requested.Count == 0
                ? addressable
                : addressable.Where(session => requested.Contains(session.FileKey!)).ToList();

            var missing = requested
                .Where(key => !addressable.Any(session => session.FileKey == key))
                .ToList();

            if (targets.Count == 0)
            {
                return ToolResponse.Error(
                    "None of the named files is connected.",
                    $"Requested: {string.Join(", ", requested)}. Connected: " +
                    $"{string.Join(", ", addressable.Select(session => session.FileKey))}. " +
                    "figma_list_open_files provides the current list.");
            }

            var perFileTimeout = TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1000, 30000));

            var outcomes = await Task.WhenAll(targets.Select(session =>
                RunInFileAsync(session, code, perFileTimeout, ct))).ConfigureAwait(false);

            var results = new JsonObject();
            var succeeded = 0;

            foreach (var outcome in outcomes)
            {
                results[outcome.FileKey] = outcome.Payload;

                if (outcome.Succeeded)
                {
                    succeeded++;
                }
            }

            var payload = new JsonObject
            {
                ["results"] = results,
                ["summary"] = new JsonObject
                {
                    ["targeted"] = targets.Count,
                    ["succeeded"] = succeeded,
                    ["failed"] = targets.Count - succeeded,
                },
                ["active_file_key"] = Plugin.Documents.ActiveFileKey,
                ["target_locked"] = Plugin.Documents.IsLocked,
            };

            if (missing.Count > 0)
            {
                payload["missing_file_keys"] = new JsonArray(missing.Select(key => (JsonNode)key!).ToArray());
            }

            if (unidentified.Count > 0)
            {
                payload["skipped_unidentified_files"] =
                    new JsonArray(unidentified.Select(name => (JsonNode)name!).ToArray());
            }

            return succeeded == 0
                ? ToolResponse.Error(
                    "The code did not run in any of the target files.",
                    "The per-file breakdown is in data.results; check that the plugin window is open in each of them.",
                    payload)
                : ToolResponse.Ok(payload);
        }, cancellationToken);

    /// <summary>One file of the fan-out; a failure here does not sink the others.</summary>
    private async Task<FileOutcome> RunInFileAsync(
        IOpenDocument session,
        string code,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var fileKey = session.FileKey!;

        try
        {
            var result = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = code, ["timeout"] = (int)timeout.TotalMilliseconds },
                fileKey: fileKey,
                timeout: timeout.Add(TimeSpan.FromSeconds(5)),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await Plugin.InvalidateWholeFileAsync(fileKey, cancellationToken).ConfigureAwait(false);

            return new FileOutcome(fileKey, true, new JsonObject
            {
                ["file_name"] = session.FileName,
                ["success"] = true,
                ["result"] = result?.DeepClone(),
            });
        }
        catch (Exception ex) when (ex is EditorUnavailableException or EditorCommandException or TimeoutException)
        {
            return new FileOutcome(fileKey, false, new JsonObject
            {
                ["file_name"] = session.FileName,
                ["success"] = false,
                ["error"] = ex.Message,
            });
        }
    }

    private static HashSet<string> SplitFileKeys(string? fileKeys) =>
        string.IsNullOrWhiteSpace(fileKeys)
            ? new HashSet<string>(StringComparer.Ordinal)
            : fileKeys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(key => DocumentLocator.Parse(key)?.EffectiveKey ?? key)
                .ToHashSet(StringComparer.Ordinal);

    private sealed record FileOutcome(string FileKey, bool Succeeded, JsonObject Payload);

    [McpServerTool(Name = "figma_reconnect")]
    [Description(ToolDescriptions.Session.Reconnect)]
    public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            Plugin.Documents.Unlock();

            return Task.FromResult(ToolResponse.Ok(new JsonObject
            {
                ["connected"] = Plugin.IsConnected,
                ["files"] = new JsonArray(Plugin.Documents.Sessions.Select(s => (JsonNode)s.Describe()).ToArray()),
                ["activeFileKey"] = Plugin.Documents.ActiveFileKey,
            }));
        }, cancellationToken);

    [McpServerTool(Name = "figma_reload_plugin")]
    [Description(ToolDescriptions.Session.ReloadPlugin)]
    public Task<string> ReloadPluginAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.ReloadUi, null, cancellationToken);

    [McpServerTool(Name = "figma_get_console_logs")]
    [Description(ToolDescriptions.Session.GetConsoleLogs)]
    public Task<string> GetConsoleLogsAsync(
        [Description("How many of the latest lines to return.")] int limit = 50,
        [Description("Keep only the lines of this level: log, info, warn, error.")] string? level = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            var session = Plugin.Documents.Resolve();

            if (session is null)
            {
                return Task.FromResult(NotConnected());
            }

            var logs = new JsonArray();

            foreach (var entry in session.ConsoleLogs.Tail(Math.Clamp(limit, 1, 1000)))
            {
                if (!string.IsNullOrWhiteSpace(level) &&
                    !string.Equals(entry["level"]?.GetValue<string>(), level, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                logs.Add(entry.DeepClone());
            }

            return Task.FromResult(ToolResponse.Ok(new JsonObject
            {
                ["logs"] = logs,
                ["returned"] = logs.Count,
                ["buffered"] = session.ConsoleLogs.Count,
            }));
        }, cancellationToken);

    [McpServerTool(Name = "figma_clear_console")]
    [Description(ToolDescriptions.Session.ClearConsole)]
    public Task<string> ClearConsoleAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            foreach (var session in Plugin.Documents.Sessions)
            {
                session.ConsoleLogs.Clear();
            }

            try
            {
                await Plugin.ReadAsync(EditorCommands.ClearConsole, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (EditorUnavailableException)
            {
            }

            return ToolResponse.Ok(new JsonObject { ["cleared"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_watch_console")]
    [Description(ToolDescriptions.Session.WatchConsole)]
    public Task<string> WatchConsoleAsync(
        [Description("How many seconds to wait for new lines.")] int timeoutSeconds = 10,
        [Description("Wait for at least this many new lines.")] int minEntries = 1,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var session = Plugin.Documents.Resolve();

            if (session is null)
            {
                return NotConnected();
            }

            var before = session.ConsoleLogs.Count;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 1, 120));

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (session.ConsoleLogs.Count - before >= minEntries)
                {
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            var added = Math.Max(0, session.ConsoleLogs.Count - before);
            var logs = new JsonArray(session.ConsoleLogs.Tail(added).Select(e => e.DeepClone()).ToArray());

            return ToolResponse.Ok(new JsonObject
            {
                ["newEntries"] = added,
                ["logs"] = logs,
                ["timedOut"] = added < minEntries,
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_export_node")]
    [Description(ToolDescriptions.Session.ExportNode)]
    public Task<string> ExportNodeAsync(
        [Description("Node id; a page id works too.")] string nodeId,
        [Description("Format: SVG, PDF, PNG or JPG.")] string format = "SVG",
        [Description("Render scale for PNG and JPG.")] double scale = 1,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var normalized = format.Trim().ToUpperInvariant();

            if (normalized is not ("SVG" or "PDF" or "PNG" or "JPG"))
            {
                return ToolResponse.Error($"Unknown format '{format}'.", "Expected SVG, PDF, PNG or JPG.");
            }

            var script = $$"""
                const node = await figma.getNodeByIdAsync({{JsonSerializer.Serialize(NodeId.Normalize(nodeId))}});
                if (!node) return { __error: 'Node was not found.' };
                if (node.type === 'PAGE' && typeof node.loadAsync === 'function') { await node.loadAsync(); }
                if (typeof node.exportAsync !== 'function') return { __error: 'Node type ' + node.type + ' cannot be exported.' };
                const format = {{JsonSerializer.Serialize(normalized)}};
                if (format === 'SVG') {
                  const svg = await node.exportAsync({ format: 'SVG_STRING' });
                  return { format: 'SVG', svg: svg, length: svg.length };
                }
                const settings = format === 'PDF'
                  ? { format: 'PDF' }
                  : { format: format, constraint: { type: 'SCALE', value: {{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}} } };
                const bytes = await node.exportAsync(settings);
                return { format: format, base64: figma.base64Encode(bytes), byteLength: bytes.length };
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 45000 },
                timeout: TimeSpan.FromSeconds(60),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            if (payload is JsonObject binary && binary["base64"] is not null)
            {
                return ToolResponse.Ok(screenshots.SaveAndReplace(binary));
            }

            return ToolResponse.Ok(payload ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_active_users")]
    [Description(ToolDescriptions.Session.GetActiveUsers)]
    public Task<string> GetActiveUsersAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            const string script = """
                const me = figma.currentUser
                  ? { id: figma.currentUser.id, name: figma.currentUser.name, sessionId: figma.currentUser.sessionId, color: figma.currentUser.color }
                  : null;
                const others = (figma.activeUsers || [])
                  .filter(function (u) { return !me || u.sessionId !== me.sessionId; })
                  .map(function (u) { return { id: u.id, name: u.name, sessionId: u.sessionId, color: u.color }; });
                return { me: me, others: others, totalInFile: (figma.activeUsers || []).length };
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 10000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(error);
            }

            return ToolResponse.Ok(payload ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_notify")]
    [Description(ToolDescriptions.Session.Notify)]
    public Task<string> NotifyAsync(
        [Description("Toast text shown in the Figma window.")] string message,
        [Description("How long it stays, in milliseconds.")] int timeoutMs = 4000,
        [Description("Style the toast as an error.")] bool isError = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                figma.notify({{JsonSerializer.Serialize(message)}}, { timeout: {{timeoutMs}}, error: {{(isError ? "true" : "false")}} });
                return { shown: true };
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 5000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            return payload?["__error"]?.GetValue<string>() is { } error
                ? ToolResponse.Error(error)
                : ToolResponse.Ok(payload ?? new JsonObject { ["shown"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_scroll_to_node")]
    [Description(ToolDescriptions.Session.ScrollToNode)]
    public Task<string> ScrollToNodeAsync(
        [Description("Node id to bring into view.")] string nodeId,
        [Description("Also select the node.")] bool select = false,
        [Description("Zoom level after centering; empty keeps the fitted zoom.")] double? zoom = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const node = await figma.getNodeByIdAsync({{JsonSerializer.Serialize(NodeId.Normalize(nodeId))}});
                if (!node) return { __error: 'Node was not found.' };
                let page = node;
                while (page && page.type !== 'PAGE') page = page.parent;
                if (page && page.type === 'PAGE' && page.id !== figma.currentPage.id) {
                  if (typeof page.loadAsync === 'function') await page.loadAsync();
                  await figma.setCurrentPageAsync(page);
                }
                figma.viewport.scrollAndZoomIntoView([node]);
                const zoom = {{(zoom is { } z ? z.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null")}};
                if (zoom !== null) figma.viewport.zoom = zoom;
                if ({{(select ? "true" : "false")}} && node.type !== 'PAGE') figma.currentPage.selection = [node];
                return { centered: node.id, page: figma.currentPage.name, zoom: figma.viewport.zoom };
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 15000 },
                timeout: TimeSpan.FromSeconds(20),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            return payload?["__error"]?.GetValue<string>() is { } error
                ? ToolResponse.Error(error, Messages.CheckNodeIdHint)
                : ToolResponse.Ok(payload ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_set_file_thumbnail")]
    [Description(ToolDescriptions.Session.SetFileThumbnail)]
    public Task<string> SetFileThumbnailAsync(
        [Description("Node whose render becomes the file cover; empty resets to the default.")] string? nodeId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const id = {{JsonSerializer.Serialize(nodeId is null ? null : NodeId.Normalize(nodeId))}};
                if (id === null) {
                  await figma.setFileThumbnailNodeAsync(null);
                  return { thumbnail: null };
                }
                const node = await figma.getNodeByIdAsync(id);
                if (!node) return { __error: 'Node was not found.' };
                try {
                  await figma.setFileThumbnailNodeAsync(node);
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                return { thumbnail: node.id, name: node.name };
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 10000 },
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            return payload?["__error"]?.GetValue<string>() is { } error
                ? ToolResponse.Error(error, "The node must be a frame, component or section on a canvas.")
                : ToolResponse.Ok(payload ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_take_screenshot")]
    [Description(ToolDescriptions.Session.TakeScreenshot)]
    public Task<string> TakeScreenshotAsync(
        [Description("Node id. Empty captures the current page.")] string? nodeId = null,
        [Description("Format: PNG (the default), JPG, SVG.")] string format = "PNG",
        [Description("Render scale.")] double scale = 1,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var result = await Plugin.ReadAsync(
                EditorCommands.CaptureScreenshot,
                new JsonObject
                {
                    ["nodeId"] = nodeId is null ? null : NodeId.Normalize(nodeId),
                    ["format"] = format.ToUpperInvariant(),
                    ["scale"] = scale,
                },
                timeout: TimeSpan.FromSeconds(60),
                cancellationToken: ct).ConfigureAwait(false);

            if (result?["image"] is not JsonObject image)
            {
                return ToolResponse.Ok(result ?? new JsonObject());
            }

            result["image"] = screenshots.SaveAndReplace(image);

            return ToolResponse.Ok(result);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_text_styles")]
    [Description(ToolDescriptions.Session.GetTextStyles)]
    public Task<string> GetTextStylesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetTextStyles, null, cancellationToken, TimeSpan.FromSeconds(45));
}
