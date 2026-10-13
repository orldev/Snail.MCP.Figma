using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

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
    [McpServerTool(Name = "figma_list_open_files", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(Answer<OpenFiles>))]
    [Description(ToolDescriptions.Session.ListOpenFiles)]
    public Task<CallToolResult> ListOpenFilesAsync(CancellationToken cancellationToken = default) =>
        ExecuteStructuredAsync(_ => Task.FromResult<JsonNode>(new JsonObject
        {
            ["files"] = DescribeFiles(),
            ["activeFileKey"] = Plugin.Documents.ActiveFileKey,
        }), cancellationToken);

    [McpServerTool(Name = "figma_navigate", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.Navigate)]
    public Task<string> NavigateAsync(
        [Description("Link to the Figma file or its key.")] string fileUrlOrKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = DocumentLocator.Parse(fileUrlOrKey)?.EffectiveKey ?? fileUrlOrKey.Trim();
            activeFile.Remember(fileKey);

            var bound = await Plugin.Documents.TryClaimAsync(fileKey, ct).ConfigureAwait(false);
            var others = bound ? OthersInTarget() : [];

            return ToolResponse.Ok(new JsonObject
            {
                ["fileKey"] = fileKey,
                ["pluginConnected"] = bound,
                ["othersHere"] = new JsonArray([.. others.Select(agent => (JsonNode)agent)]),
                ["note"] = TargetNote(bound, others),
            });
        }, cancellationToken);

    /// <summary>What the agent should know about the file it just took: that it stays, and who else is in it.</summary>
    private static string TargetNote(bool bound, IReadOnlyList<string> others)
    {
        if (!bound)
        {
            return "The plugin is not running in this file: reading through REST works, writing does not.";
        }

        return others.Count == 0
            ? "The file is the target: commands go here until you navigate elsewhere, and the human's clicks do not move it."
            : $"The file is the target, and {string.Join(", ", others)} also works here: keep to your own page or section.";
    }

    [McpServerTool(Name = "figma_execute_across_files", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Session.ExecuteAcrossFiles)]
    public Task<string> ExecuteAcrossFilesAsync(
        [Description("JavaScript code, the same as in figma_execute. Example: return figma.root.children.length;")]
        string code,
        [Description("Comma-separated keys of the target files. They come from figma_list_open_files.")] string? fileKeys = null,
        [Description("Run it over EVERY connected file. Requires explicit consent: the code will reach the target file too.")]
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
                : [.. addressable.Where(session => requested.Contains(session.FileKey!))];

            var connectedKeys = addressable.Select(session => session.FileKey).ToHashSet(StringComparer.Ordinal);
            var missing = requested
                .Where(key => !connectedKeys.Contains(key))
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

    [McpServerTool(Name = "figma_reconnect", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.Reconnect)]
    public Task<string> ReconnectAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ => Task.FromResult(ToolResponse.Ok(new JsonObject
        {
            ["connected"] = Plugin.IsConnected,
            ["files"] = DescribeFiles(),
            ["activeFileKey"] = Plugin.Documents.ActiveFileKey,
        })), cancellationToken);

    [McpServerTool(Name = "figma_reload_plugin", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.ReloadPlugin)]
    public Task<string> ReloadPluginAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.ReloadUi, null, cancellationToken);

    [McpServerTool(Name = "figma_get_console_logs", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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

            var visible = session.ConsoleLogs.Since(Plugin.Documents.ConsoleMark(session));
            var logs = new JsonArray();

            foreach (var entry in visible.Skip(Math.Max(0, visible.Count - Math.Clamp(limit, 1, 1000))))
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
                ["buffered"] = visible.Count,
            }));
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_remarks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.GetRemarks)]
    public Task<string> GetRemarksAsync(
        [Description("How long to hold the call open when nothing is waiting yet, in milliseconds, up to 60000. 0 answers straight away; waiting is how the agent hears the human within a second instead of at its next turn.")]
        int waitMs = 0,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var session = Plugin.Documents.Resolve();

            if (session is null)
            {
                return NotConnected();
            }

            if (waitMs is < 0 or > ToolLimits.MaxRemarkWaitMs)
            {
                return ToolResponse.Error($"waitMs must be between 0 and {ToolLimits.MaxRemarkWaitMs}.");
            }

            var remarks = Plugin.Documents.RemarksIn(session);

            if (waitMs > 0)
            {
                await remarks.TryWaitAsync(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);
            }

            var taken = remarks.TakeAll();

            await Plugin.Documents.PublishRemarksWaitingAsync(session, ct).ConfigureAwait(false);

            return ToolResponse.Ok(new JsonObject
            {
                ["remarks"] = new JsonArray([.. taken.Select(Describe)]),
                ["returned"] = taken.Count,
                ["note"] = taken.Count == 0
                    ? waitMs > 0
                        ? $"Nobody wrote anything in {waitMs} ms. The panel is connected and the wait is over, not broken."
                        : "The panel is connected and nobody has left a remark since the last read."
                    : "These remarks are taken: they will not come back, so act on them in this turn.",
            });
        }, cancellationToken);

    /// <summary>One remark as the agent reads it: the words, what they point at, and when they were left.</summary>
    private static JsonNode Describe(BuildInterruption remark) => new JsonObject
    {
        ["at"] = remark.At.ToString("O"),
        ["remark"] = remark.Remark,
        ["selection"] = new JsonArray([.. remark.Nodes.Select(node => node.DeepClone())]),
    };

    [McpServerTool(Name = "figma_clear_console", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.ClearConsole)]
    public Task<string> ClearConsoleAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            foreach (var session in Plugin.Documents.Sessions)
            {
                Plugin.Documents.ClearConsole(session);
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

    [McpServerTool(Name = "figma_watch_console", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
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

            var before = Math.Max(session.ConsoleLogs.Added, Plugin.Documents.ConsoleMark(session));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 1, 120));

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (session.ConsoleLogs.Added - before >= minEntries)
                {
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            var arrived = session.ConsoleLogs.Since(before);
            var added = arrived.Count;
            var logs = new JsonArray([.. arrived.Select(entry => entry.DeepClone())]);

            return ToolResponse.Ok(new JsonObject
            {
                ["newEntries"] = added,
                ["logs"] = logs,
                ["timedOut"] = added < minEntries,
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_export_node", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.ExportNode)]
    public Task<CallToolResult> ExportNodeAsync(
        [Description("Node id; a page id works too.")] string nodeId,
        [Description("Format: SVG, PDF, PNG or JPG.")] string format = "SVG",
        [Description("Render scale for PNG and JPG.")] double scale = 1,
        [Description(ToolDescriptions.Parameters.Deliver)] string deliver = PictureDelivery.PathOnly,
        CancellationToken cancellationToken = default) =>
        ExecutePicturedAsync(async ct =>
        {
            if (!PictureDelivery.TryParse(deliver, out var inline, out var deliverError))
            {
                return ToolResponse.Textual(ToolResponse.Error(deliverError!, PictureDelivery.Hint));
            }

            var normalized = format.Trim().ToUpperInvariant();

            if (normalized is not ("SVG" or "PDF" or "PNG" or "JPG"))
            {
                return ToolResponse.Textual(ToolResponse.Error($"Unknown format '{format}'.", "Expected SVG, PDF, PNG or JPG."));
            }

            var script = $$"""
                const node = await figma.getNodeByIdAsync({{ScriptLiteral.Of(NodeId.Normalize(nodeId))}});
                if (!node) return { __error: 'Node was not found.' };
                if (node.type === 'PAGE' && typeof node.loadAsync === 'function') { await node.loadAsync(); }
                if (typeof node.exportAsync !== 'function') return { __error: 'Node type ' + node.type + ' cannot be exported.' };
                const format = {{ScriptLiteral.Of(normalized)}};
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
                return ToolResponse.Textual(ToolResponse.Error(error));
            }

            if (payload is JsonObject binary && binary["base64"] is not null)
            {
                var picture = PictureDelivery.From(binary, inline);

                return ToolResponse.Pictured(ToolResponse.Ok(screenshots.SaveAndReplace(binary)), picture);
            }

            return ToolResponse.Textual(ToolResponse.Ok(payload ?? new JsonObject()));
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_active_users", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
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

    [McpServerTool(Name = "figma_notify", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Session.Notify)]
    public Task<string> NotifyAsync(
        [Description("Toast text shown in the Figma window.")] string message,
        [Description("How long it stays, in milliseconds.")] int timeoutMs = 4000,
        [Description("Style the toast as an error.")] bool isError = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                figma.notify({{ScriptLiteral.Of(message)}}, { timeout: {{timeoutMs}}, error: {{(isError ? "true" : "false")}} });
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

    [McpServerTool(Name = "figma_scroll_to_node", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.ScrollToNode)]
    public Task<string> ScrollToNodeAsync(
        [Description("Node id to bring into view.")] string nodeId,
        [Description("Also select the node.")] bool select = false,
        [Description("Zoom level after centering; empty keeps the fitted zoom.")] double? zoom = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const node = await figma.getNodeByIdAsync({{ScriptLiteral.Of(NodeId.Normalize(nodeId))}});
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

    [McpServerTool(Name = "figma_set_file_thumbnail", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.SetFileThumbnail)]
    public Task<string> SetFileThumbnailAsync(
        [Description("Node whose render becomes the file cover; empty resets to the default.")] string? nodeId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                const id = {{ScriptLiteral.Of(nodeId is null ? null : NodeId.Normalize(nodeId))}};
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

    [McpServerTool(Name = "figma_take_screenshot", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.TakeScreenshot)]
    public Task<CallToolResult> TakeScreenshotAsync(
        [Description("Node id. Empty captures the current page.")] string? nodeId = null,
        [Description("Format: PNG (the default), JPG, SVG.")] string format = "PNG",
        [Description("Render scale.")] double scale = 1,
        [Description(ToolDescriptions.Parameters.Deliver)] string deliver = PictureDelivery.PathOnly,
        CancellationToken cancellationToken = default) =>
        ExecutePicturedAsync(async ct =>
        {
            if (!PictureDelivery.TryParse(deliver, out var inline, out var deliverError))
            {
                return ToolResponse.Textual(ToolResponse.Error(deliverError!, PictureDelivery.Hint));
            }

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
                return ToolResponse.Textual(ToolResponse.Ok(result ?? new JsonObject()));
            }

            var picture = PictureDelivery.From(image, inline);

            result["image"] = screenshots.SaveAndReplace(image);

            return ToolResponse.Pictured(ToolResponse.Ok(result), picture);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_text_styles", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Session.GetTextStyles)]
    public Task<string> GetTextStylesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(EditorCommands.GetTextStyles, null, cancellationToken, TimeSpan.FromSeconds(45));
}
