using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Extraction;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Team libraries: published components by key via REST, library variables via the plugin only.</summary>
[McpServerToolType]
public sealed partial class LibraryTools(
    ILiveEditor plugin,
    IPublishedDocumentSource api,
    ILibraryCatalog catalog,
    ILogger<LibraryTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_get_library_component_by_key")]
    [Description(ToolDescriptions.Library.GetLibraryComponentByKey)]
    public Task<string> GetLibraryComponentByKeyAsync(
        [Description("Component key.")] string componentKey,
        [Description("Include visual specifications of the variants.")] bool includeVisualSpecs = true,
        [Description("Response size: full or summary.")] string format = "full",
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var warnings = new JsonArray();

            var resolvedAs = "COMPONENT_SET";
            JsonNode? meta = null;

            try
            {
                meta = (await catalog.GetPublishedComponentSetAsync(componentKey, ct).ConfigureAwait(false))["meta"];
            }
            catch (PublishedSourceException ex) when (ex.Failure == SourceFailure.NotFound)
            {
                try
                {
                    meta = (await catalog.GetPublishedComponentAsync(componentKey, ct).ConfigureAwait(false))["meta"];
                    resolvedAs = "COMPONENT";
                }
                catch (PublishedSourceException inner) when (inner.Failure == SourceFailure.NotFound)
                {
                    return ToolResponse.Error(
                        $"No component with key {componentKey} was found.",
                        "Check that the key came from figma_search_components and that the component is published: " +
                        "draft and unpublished components are not readable through this endpoint.");
                }
            }

            var fileKey = meta?["file_key"]?.GetValue<string>();
            var nodeId = meta?["node_id"]?.GetValue<string>();

            if (fileKey is null || nodeId is null)
            {
                return ToolResponse.Error(
                    "Figma returned a component description without file_key or node_id.",
                    "Check the key with figma_search_components.");
            }

            JsonNode? document = null;

            try
            {
                var response = await api
                    .GetNodesAsync(fileKey, [nodeId], new NodeReadOptions { Depth = 2 }, ct)
                    .ConfigureAwait(false);
                document = response["nodes"]?[nodeId]?["document"];
            }
            catch (PublishedSourceException ex)
            {
                warnings.Add($"The node tree did not read: {ex.Message}");
            }

            var variantKeys = new Dictionary<string, string>(StringComparer.Ordinal);

            if (resolvedAs == "COMPONENT_SET")
            {
                try
                {
                    var components = (await api.GetComponentsAsync(fileKey, ct).ConfigureAwait(false))
                        ["meta"]?["components"] as JsonArray ?? [];

                    foreach (var component in components)
                    {
                        var id = component?["node_id"]?.GetValue<string>();
                        var key = component?["key"]?.GetValue<string>();

                        if (id is not null && key is not null)
                        {
                            variantKeys[id] = key;
                        }
                    }
                }
                catch (PublishedSourceException ex)
                {
                    warnings.Add(
                        $"The component list of the file did not read ({ex.Message}) — variants come back without published keys.");
                }
            }

            var summaryOnly = string.Equals(format, "summary", StringComparison.OrdinalIgnoreCase);
            var withSpecs = includeVisualSpecs && !summaryOnly;

            var variants = new JsonArray();

            foreach (var child in document?["children"] as JsonArray ?? [])
            {
                var id = child?["id"]?.GetValue<string>() ?? string.Empty;
                var variant = new JsonObject
                {
                    ["name"] = child?["name"]?.DeepClone(),
                    ["nodeId"] = id,
                    ["key"] = variantKeys.GetValueOrDefault(id),
                };

                if (withSpecs && DesignSystemKit.ExtractVisualSpec(child) is { } spec)
                {
                    variant["visualSpec"] = spec;
                }

                variants.Add(variant);
            }

            if (variants.Count > 0 && withSpecs)
            {
                DesignSystemKit.DeltaEncodeVariants(variants);
            }

            var payload = new JsonObject
            {
                ["componentKey"] = componentKey,
                ["resolvedAs"] = resolvedAs,
                ["fileKey"] = fileKey,
                ["nodeId"] = nodeId,
                ["name"] = meta?["name"]?.DeepClone(),
                ["description"] = meta?["description"]?.DeepClone(),
                ["thumbnailUrl"] = meta?["thumbnail_url"]?.DeepClone(),
                ["containingFrame"] = meta?["containing_frame"]?.DeepClone(),
                ["user"] = meta?["user"]?.DeepClone(),
                ["createdAt"] = meta?["created_at"]?.DeepClone(),
                ["updatedAt"] = meta?["updated_at"]?.DeepClone(),
                ["properties"] = document?["componentPropertyDefinitions"]?.DeepClone(),
                ["variants"] = variants,
                ["usage"] = resolvedAs == "COMPONENT_SET"
                    ? "For a set, insert the key of the VARIANT from variants[], not the componentKey of the set."
                    : "For a single component pass componentKey straight to figma_instantiate_component.",
                ["warnings"] = warnings,
            };

            if (withSpecs && DesignSystemKit.ExtractVisualSpec(document) is { } rootSpec)
            {
                payload["visualSpec"] = rootSpec;
            }

            var sizeKb = DesignSystemKit.SizeKb(payload);

            if (sizeKb > 500)
            {
                foreach (var variant in payload["variants"]!.AsArray())
                {
                    variant?.AsObject().Remove("visualSpec");
                    variant?.AsObject().Remove("visualSpecDelta");
                }

                warnings.Add(
                    $"The response weighed {DesignSystemKit.FormatKb(sizeKb)} KB — the visual specifications of the variants were dropped. " +
                    "Take a single variant through figma_get_component when you need its exact values.");
            }

            return ToolResponse.Ok(payload);
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_library_variables")]
    [Description(ToolDescriptions.Library.GetLibraryVariables)]
    public Task<string> GetLibraryVariablesAsync(
        [Description("Filter by library name.")] string? libraryName = null,
        [Description("Filter by collection name inside the library.")] string? collectionName = null,
        [Description("Filter by type: COLOR, FLOAT, STRING, BOOLEAN.")] string? resolvedType = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            const string script = """
                if (!figma.teamLibrary || typeof figma.teamLibrary.getAvailableLibraryVariableCollectionsAsync !== 'function') {
                  return { __error: 'figma.teamLibrary API not available. Requires a recent Figma Desktop build.' };
                }
                const collections = await figma.teamLibrary.getAvailableLibraryVariableCollectionsAsync();
                const out = [];
                for (let i = 0; i < collections.length; i++) {
                  const coll = collections[i];
                  let vars = [];
                  try {
                    vars = await figma.teamLibrary.getVariablesInLibraryCollectionAsync(coll.key);
                  } catch (innerErr) {
                    out.push({
                      libraryName: coll.libraryName || null,
                      collectionKey: coll.key,
                      collectionName: coll.name,
                      error: innerErr && innerErr.message ? innerErr.message : String(innerErr),
                      variables: [],
                      variableCount: 0,
                    });
                    continue;
                  }
                  out.push({
                    libraryName: coll.libraryName || null,
                    collectionKey: coll.key,
                    collectionName: coll.name,
                    variableCount: vars.length,
                    variables: vars.map(function (v) {
                      return { key: v.key, name: v.name, resolvedType: v.resolvedType };
                    }),
                  });
                }
                return out;
                """;

            var raw = await Plugin.ReadAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 30000 },
                timeout: TimeSpan.FromSeconds(45),
                cancellationToken: ct).ConfigureAwait(false);

            var payload = Unwrap(raw);

            if (payload?["__error"]?.GetValue<string>() is { } error)
            {
                return ToolResponse.Error(
                    error,
                    "A recent Figma Desktop build with the teamLibrary API and at least one connected library is required.");
            }

            var collections = new JsonArray();
            var totalVariables = 0;

            foreach (var collection in payload as JsonArray ?? [])
            {
                if (!Matches(collection?["libraryName"]?.GetValue<string>(), libraryName)
                    || !Matches(collection?["collectionName"]?.GetValue<string>(), collectionName))
                {
                    continue;
                }

                var variables = new JsonArray((collection?["variables"] as JsonArray ?? [])
                    .Where(variable => resolvedType is null
                        || string.Equals(variable?["resolvedType"]?.GetValue<string>(), resolvedType, StringComparison.OrdinalIgnoreCase))
                    .Select(variable => variable!.DeepClone())
                    .ToArray());

                totalVariables += variables.Count;

                collections.Add(new JsonObject
                {
                    ["libraryName"] = collection?["libraryName"]?.DeepClone(),
                    ["collectionKey"] = collection?["collectionKey"]?.DeepClone(),
                    ["collectionName"] = collection?["collectionName"]?.DeepClone(),
                    ["error"] = collection?["error"]?.DeepClone(),
                    ["variableCount"] = variables.Count,
                    ["variables"] = variables,
                });
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["collections"] = collections,
                ["summary"] = new JsonObject
                {
                    ["collections"] = collections.Count,
                    ["variables"] = totalVariables,
                },
                ["nextStep"] = "Pass the key of the variable you need to figma_import_library_variable — after the import its id " +
                               "is accepted by every tool that binds variables to nodes.",
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_import_library_variable")]
    [Description(ToolDescriptions.Library.ImportLibraryVariable)]
    public Task<string> ImportLibraryVariableAsync(
        [Description("Variable key from figma_get_library_variables.")] string variableKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var script = $$"""
                if (!figma.variables || typeof figma.variables.importVariableByKeyAsync !== 'function') {
                  return { __error: 'figma.variables.importVariableByKeyAsync not available.' };
                }
                try {
                  const imported = await figma.variables.importVariableByKeyAsync({{JsonSerializer.Serialize(variableKey)}});
                  if (!imported) {
                    return { __error: 'Import returned no variable. The key may be invalid or the source library is not subscribed by this file.' };
                  }
                  return {
                    id: imported.id,
                    key: imported.key,
                    name: imported.name,
                    resolvedType: imported.resolvedType,
                    description: imported.description || null,
                    variableCollectionId: imported.variableCollectionId,
                    remote: imported.remote === true,
                  };
                } catch (e) {
                  return { __error: (e && e.message) ? e.message : String(e) };
                }
                """;

            var raw = await Plugin.ApplyAsync(
                EditorCommands.ExecuteCode,
                new JsonObject { ["code"] = script, ["timeout"] = 20000 },
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: ct).ConfigureAwait(false);

            var imported = Unwrap(raw);

            if (imported?["__error"]?.GetValue<string>() is { } error)
            {
                var hint = error.Contains("subscrib", StringComparison.OrdinalIgnoreCase)
                    || error.Contains("access", StringComparison.OrdinalIgnoreCase)
                    ? "The library holding this variable is not connected to the current file. Connect it in Figma: Assets → Libraries, and retry."
                    : "Check that the key came from figma_get_library_variables and that the library is connected to the file.";

                return ToolResponse.Error(error, hint);
            }

            if (imported?["id"]?.GetValue<string>() is not { Length: > 0 })
            {
                return ToolResponse.Error(
                    "The plugin returned an import result without a variable id.",
                    "Retry the call; if it keeps happening, check the key with figma_get_library_variables.");
            }

            return ToolResponse.Ok(imported.DeepClone());
        }, cancellationToken);

    /// <summary>Unwraps the plugin envelope <c>{ success, result }</c> around code execution.</summary>
    [McpServerTool(Name = "figma_get_library_style_by_key")]
    [Description(ToolDescriptions.Library.GetLibraryStyleByKey)]
    public Task<string> GetLibraryStyleByKeyAsync(
        [Description("Style key.")] string styleKey,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            JsonNode? meta;

            try
            {
                meta = (await catalog.GetPublishedStyleAsync(styleKey, ct).ConfigureAwait(false))["meta"];
            }
            catch (PublishedSourceException ex) when (ex.Failure is SourceFailure.NotFound or SourceFailure.Forbidden)
            {
                return ToolResponse.Error(
                    $"Figma did not return a style for key {styleKey} ({ex.Failure}).",
                    "Either the key is wrong, the style is not published, or the token has no access to its library. The key of a published style comes from figma_get_styles (the key field).");
            }

            if (meta is null)
            {
                return ToolResponse.Error("Figma returned a style without the meta block.");
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["key"] = meta["key"]?.DeepClone(),
                ["name"] = meta["name"]?.DeepClone(),
                ["styleType"] = meta["style_type"]?.DeepClone(),
                ["description"] = meta["description"]?.DeepClone(),
                ["fileKey"] = meta["file_key"]?.DeepClone(),
                ["nodeId"] = meta["node_id"]?.DeepClone(),
                ["updatedAt"] = meta["updated_at"]?.DeepClone(),
                ["creator"] = meta["user"]?["handle"]?.DeepClone(),
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_search_team_components")]
    [Description(ToolDescriptions.Library.SearchTeamComponents)]
    public Task<string> SearchTeamComponentsAsync(
        [Description("Team id, or a figma.com link containing /team/{id}/.")] string teamIdOrUrl,
        [Description("Substring of the component name or description. Empty returns the whole page.")] string? query = null,
        [Description("Also search component sets.")] bool includeComponentSets = true,
        [Description("Page size per kind, 1-1000.")] int pageSize = 100,
        [Description("Pagination cursor from the previous response.")] string? after = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseTeamId(teamIdOrUrl, out var teamId))
            {
                return ToolResponse.Error(
                    $"Could not read a team id from '{teamIdOrUrl}'.",
                    "Pass the digits or a link like https://www.figma.com/files/team/1234567890/Name. " +
                    "The id is in the address bar when the team page is open.");
            }

            var size = Math.Clamp(pageSize, 1, 1000);
            var items = new JsonArray();
            string? nextCursor = null;

            async Task Collect(Task<JsonNode> request, string kind)
            {
                var response = await request.ConfigureAwait(false);
                nextCursor ??= response["meta"]?["cursor"]?["after"]?.GetValue<string>()
                    ?? (response["cursor"] as JsonObject)?["after"]?.GetValue<string>();

                var list = response["meta"]?[kind] as JsonArray ?? response[kind] as JsonArray ?? [];

                foreach (var item in list)
                {
                    var name = item?["name"]?.GetValue<string>() ?? string.Empty;
                    var description = item?["description"]?.GetValue<string>() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(query)
                        && !name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(new JsonObject
                    {
                        ["kind"] = kind == "components" ? "COMPONENT" : "COMPONENT_SET",
                        ["key"] = item?["key"]?.DeepClone(),
                        ["name"] = name,
                        ["description"] = string.IsNullOrWhiteSpace(description) ? null : description,
                        ["fileKey"] = item?["file_key"]?.DeepClone(),
                        ["nodeId"] = item?["node_id"]?.DeepClone(),
                    });
                }
            }

            await Collect(catalog.GetTeamComponentsPageAsync(teamId, size, after, ct), "components").ConfigureAwait(false);

            if (includeComponentSets)
            {
                await Collect(catalog.GetTeamComponentSetsPageAsync(teamId, size, after, ct), "component_sets").ConfigureAwait(false);
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["teamId"] = teamId,
                ["matched"] = items.Count,
                ["items"] = items,
                ["nextCursor"] = nextCursor,
            }, envelope =>
            {
                if (items.Count == 0)
                {
                    envelope["hint"] = "Nothing matched on this page. Widen the query, or continue with the nextCursor value.";
                }
            });
        }, cancellationToken);

    [GeneratedRegex(@"/team/(\d+)")]
    private static partial Regex TeamIdInUrlPattern();

    /// <summary>Accepts raw digits or a figma.com URL with /team/{id}/.</summary>
    internal static bool TryParseTeamId(string input, out string teamId)
    {
        teamId = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;

        if (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit))
        {
            teamId = trimmed;

            return true;
        }

        if (TeamIdInUrlPattern().Match(trimmed) is { Success: true } match)
        {
            teamId = match.Groups[1].Value;

            return true;
        }

        return false;
    }

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || (value is not null && value.Contains(filter, StringComparison.OrdinalIgnoreCase));
}
