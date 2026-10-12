using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Sanitization;
using Snail.MCP.Figma.Configuration;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Bridge between design and code: implementation specs, design-versus-code checks, docs and token-like values from the tree.</summary>
[McpServerToolType]
public sealed class DesignCodeTools(
    ILiveEditor plugin,
    IPublishedDocumentSource api,
    DocumentReader reader,
    ActiveDocumentTracker activeFile,
    SanitizationOptions sanitizationOptions,
    ILogger<DesignCodeTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_get_component_for_development", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.DesignCode.GetComponentForDevelopment)]
    public Task<string> GetForDevelopmentAsync(
        [Description("Node id, for example '1:234'.")] string nodeId,
        [Description("Link to the file. Needed only when the plugin is not running.")] string? fileUrl = null,
        [Description("Tree traversal depth.")] int depth = 4,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var normalized = NodeId.Normalize(nodeId);
            var node = await LoadNodeAsync(normalized, fileUrl, depth, ct).ConfigureAwait(false);

            if (node is null)
            {
                return ToolResponse.Error(
                    $"Node {normalized} was not found.",
                    "Check the id with figma_get_selection or figma_search_components.");
            }

            var spec = ReconstructionSpec.Extract(node);
            var (valid, errors) = ReconstructionSpec.Validate(spec);
            var styles = new StyleExtractor().Extract(node, depth + 2);

            return ToolResponse.Ok(new JsonObject
            {
                ["node"] = node.DeepClone(),
                ["reconstructionSpec"] = spec,
                ["specValid"] = valid,
                ["specErrors"] = new JsonArray(errors.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
                ["styles"] = StyleExtractor.Format(styles),
            }, envelope => envelope["nodeId"] = normalized);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_component_for_development_deep", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignCode.GetComponentForDevelopmentDeep)]
    public Task<string> GetForDevelopmentDeepAsync(
        [Description("Node id.")] string nodeId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var normalized = NodeId.Normalize(nodeId);

            var node = await Plugin.ReadAsync(
                EditorCommands.DeepGetComponent,
                new JsonObject { ["nodeId"] = normalized },
                timeout: TimeSpan.FromSeconds(60),
                cancellationToken: ct).ConfigureAwait(false);

            if (node is null)
            {
                return ToolResponse.Error($"The plugin returned no data for node {normalized}.");
            }

            var payload = node["node"] ?? node;
            var spec = ReconstructionSpec.Extract(payload);

            return ToolResponse.Ok(new JsonObject
            {
                ["node"] = node.DeepClone(),
                ["reconstructionSpec"] = spec,
                ["styles"] = StyleExtractor.Format(new StyleExtractor().Extract(payload)),
            }, envelope => envelope["nodeId"] = normalized);
        }, cancellationToken);

    [McpServerTool(Name = "figma_extract_style_values", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.DesignCode.ExtractStyleValues)]
    public Task<string> ExtractTokensAsync(
        [Description("Link to the Figma file. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Id of the root node. Empty means the whole file.")] string? nodeId = null,
        [Description("Traversal depth.")] int depth = 8,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var fileKey = activeFile.Resolve(fileUrl);

            JsonNode source;

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                var read = await reader.ReadFileAsync(
                    fileKey,
                    new DocumentReadOptions { Depth = depth },
                    new SanitizationRequest
                    {
                        Profile = SanitizationProfile.Balanced,
                        MaxDepth = depth,
                        MaxNodes = 5000,
                        DeduplicateStyles = false,
                    },
                    ct).ConfigureAwait(false);

                source = read.Node;
            }
            else
            {
                var response = await api.GetNodesAsync(
                    fileKey,
                    [NodeId.Normalize(nodeId)],
                    new NodeReadOptions { Depth = depth },
                    ct).ConfigureAwait(false);

                source = response["nodes"]?[NodeId.Normalize(nodeId)]?["document"] ?? response;
            }

            var styles = new StyleExtractor().Extract(source, depth + 2);

            return ToolResponse.Ok(StyleExtractor.Format(styles), envelope => envelope["fileKey"] = fileKey);
        }, cancellationToken);

    [McpServerTool(Name = "figma_check_design_parity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.DesignCode.CheckDesignParity)]
    public Task<string> CheckParityAsync(
        [Description("Id of the component node in Figma.")] string nodeId,
        [Description("Code-side data as a JSON string.")] string codeSpec,
        [Description("Link to the file. Needed only when the plugin is not running.")] string? fileUrl = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(codeSpec, out var parsed, out var error) || parsed is not JsonObject code)
            {
                return ToolResponse.Error(
                    $"codeSpec did not parse as a JSON object: {error ?? "the value is not an object"}",
                    "An object of the form {\"fontSize\":14,\"paddingLeft\":16} is expected.");
            }

            var normalized = NodeId.Normalize(nodeId);
            var node = await LoadNodeAsync(normalized, fileUrl, depth: 4, ct).ConfigureAwait(false);

            if (node is null)
            {
                return ToolResponse.Error($"Node {normalized} was not found.");
            }

            var design = ReconstructionSpec.Extract(node);
            var report = ParityReport.Compare(design, code);

            return ToolResponse.Ok(report, envelope => envelope["nodeId"] = normalized);
        }, cancellationToken);

    [McpServerTool(Name = "figma_generate_component_doc", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.DesignCode.GenerateComponentDoc)]
    public Task<string> GenerateComponentDocAsync(
        [Description("Id of the component node.")] string nodeId,
        [Description("Link to the file. Needed only when the plugin is not running.")] string? fileUrl = null,
        [Description("Code-side data as a JSON string.")] string? codeInfo = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(codeInfo, out var parsedCode, out var error))
            {
                return ToolResponse.Error($"codeInfo did not parse as JSON: {error}");
            }

            var normalized = NodeId.Normalize(nodeId);
            var node = await LoadNodeAsync(normalized, fileUrl, depth: 5, ct).ConfigureAwait(false);

            if (node is null)
            {
                return ToolResponse.Error($"Node {normalized} was not found.");
            }

            var markdown = ComponentDoc.Render(node, new StyleExtractor().Extract(node), parsedCode as JsonObject);

            return ToolResponse.Ok(new JsonObject
            {
                ["markdown"] = markdown,
                ["nodeId"] = normalized,
                ["name"] = node["name"]?.DeepClone(),
            });
        }, cancellationToken);

    /// <summary>Node from the plugin when it runs, otherwise from REST; the plugin shows the current state, REST the saved one.</summary>
    private async Task<JsonNode?> LoadNodeAsync(string nodeId, string? fileUrl, int depth, CancellationToken cancellationToken)
    {
        if (Plugin.IsConnected)
        {
            try
            {
                var fromPlugin = await Plugin.ReadAsync(
                    EditorCommands.GetComponent,
                    new JsonObject { ["nodeId"] = nodeId },
                    timeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var payload = fromPlugin?["node"] ?? fromPlugin;

                if (payload is JsonObject { Count: > 0 })
                {
                    return payload;
                }
            }
            catch (Exception ex) when (ex is EditorCommandException or TimeoutException or EditorUnavailableException)
            {
                Logger.LogInformation("Plugin did not return node {NodeId}, reading through REST: {Message}", nodeId, ex.Message);
            }
        }

        var fileKey = activeFile.Resolve(fileUrl);
        var read = await reader.ReadNodeAsync(
            fileKey,
            nodeId,
            new NodeReadOptions { Depth = depth },
            new SanitizationRequest
            {
                Profile = SanitizationProfile.Balanced,
                MaxDepth = depth,
                MaxNodes = sanitizationOptions.DefaultMaxNodes * 4,
                DeduplicateStyles = false,
            },
            cancellationToken).ConfigureAwait(false);

        return read.Node;
    }
}
