using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Variables, collections and modes — all Plugin API, since Variables REST is plan-gated and read-only.</summary>
[McpServerToolType]
public sealed class VariableTools(ILiveEditor plugin, ILogger<VariableTools> logger)
    : EditorToolBase(plugin, logger)
{
    [McpServerTool(Name = "figma_create_variable_collection", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.CreateVariableCollection)]
    public Task<string> CreateCollectionAsync(
        [Description("Collection name, for example 'Colors' or 'Spacing'.")] string name,
        [Description("Name of the first mode. By default Figma calls it 'Mode 1'.")] string? initialModeName = null,
        [Description("Additional modes, comma-separated, for example 'Dark, High Contrast'.")] string? additionalModes = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.CreateVariableCollection, new JsonObject
        {
            ["name"] = name,
            ["initialModeName"] = initialModeName,
            ["additionalModes"] = SplitList(additionalModes),
        }, cancellationToken);

    [McpServerTool(Name = "figma_delete_variable_collection", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.DeleteVariableCollection)]
    public Task<string> DeleteCollectionAsync(
        [Description("Collection id, for example 'VariableCollectionId:1:2'.")] string collectionId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteVariableCollection,
            new JsonObject { ["collectionId"] = collectionId }, cancellationToken);

    [McpServerTool(Name = "figma_create_variable", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.CreateVariable)]
    public Task<string> CreateVariableAsync(
        [Description("Variable name, for example 'color/brand/primary'.")] string name,
        [Description("Collection id.")] string collectionId,
        [Description("Type: COLOR, FLOAT, STRING, BOOLEAN.")] string resolvedType,
        [Description("Values per mode, JSON of the form {\"1:0\":\"#FF0000\",\"1:1\":\"#0000FF\"}.")] string? valuesByMode = null,
        [Description("Variable description.")] string? description = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(valuesByMode, out var values, out var error))
            {
                return ToolResponse.Error($"valuesByMode did not parse as JSON: {error}",
                    "An object of the form {\"1:0\":\"#FF0000\"} is expected.");
            }

            var result = await Plugin.ApplyAsync(EditorCommands.CreateVariable, new JsonObject
            {
                ["name"] = name,
                ["collectionId"] = collectionId,
                ["resolvedType"] = resolvedType.ToUpperInvariant(),
                ["valuesByMode"] = values,
                ["description"] = description,
            }, cancellationToken: ct).ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject { ["success"] = true });
        }, cancellationToken);

    [McpServerTool(Name = "figma_update_variable", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.UpdateVariable)]
    public Task<string> UpdateVariableAsync(
        [Description("Variable id, for example 'VariableID:1:23'.")] string variableId,
        [Description("Mode id, for example '1:0'.")] string modeId,
        [Description("New value: '#FF0000', a number, true/false or JSON.")] string value,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.UpdateVariable, new JsonObject
        {
            ["variableId"] = variableId,
            ["modeId"] = modeId,
            ["value"] = ParseScalar(value),
        }, cancellationToken);

    [McpServerTool(Name = "figma_delete_variable", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.DeleteVariable)]
    public Task<string> DeleteVariableAsync(
        [Description("Variable id.")] string variableId,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.DeleteVariable,
            new JsonObject { ["variableId"] = variableId }, cancellationToken);

    [McpServerTool(Name = "figma_rename_variable", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.RenameVariable)]
    public Task<string> RenameVariableAsync(
        [Description("Variable id.")] string variableId,
        [Description("New name, for example 'color/brand/primary'.")] string newName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.RenameVariable,
            new JsonObject { ["variableId"] = variableId, ["newName"] = newName }, cancellationToken);

    [McpServerTool(Name = "figma_set_variable_description", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.SetVariableDescription)]
    public Task<string> SetVariableDescriptionAsync(
        [Description("Variable id.")] string variableId,
        [Description("Description text.")] string description,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.SetVariableDescription,
            new JsonObject { ["variableId"] = variableId, ["description"] = description }, cancellationToken);

    [McpServerTool(Name = "figma_add_mode", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.AddMode)]
    public Task<string> AddModeAsync(
        [Description("Collection id.")] string collectionId,
        [Description("Mode name, for example 'Dark'.")] string modeName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.AddMode,
            new JsonObject { ["collectionId"] = collectionId, ["modeName"] = modeName }, cancellationToken);

    [McpServerTool(Name = "figma_rename_mode", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.RenameMode)]
    public Task<string> RenameModeAsync(
        [Description("Collection id.")] string collectionId,
        [Description("Mode id.")] string modeId,
        [Description("New mode name.")] string newName,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(EditorCommands.RenameMode, new JsonObject
        {
            ["collectionId"] = collectionId,
            ["modeId"] = modeId,
            ["newName"] = newName,
        }, cancellationToken);

    [McpServerTool(Name = "figma_get_token_values", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.GetTokenValues)]
    public Task<string> GetTokenValuesAsync(
        [Description("Re-read the data in the plugin before reading.")] bool refresh = false,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (refresh)
            {
                await Plugin.ReadAsync(EditorCommands.RefreshVariables, cancellationToken: ct).ConfigureAwait(false);
            }

            var result = await Plugin.ReadAsync(EditorCommands.GetVariablesData, cancellationToken: ct)
                .ConfigureAwait(false);

            return ToolResponse.Ok(result ?? new JsonObject());
        }, cancellationToken);

    [McpServerTool(Name = "figma_batch_create_variables", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.BatchCreateVariables)]
    public Task<string> BatchCreateVariablesAsync(
        [Description("Collection id.")] string collectionId,
        [Description("Array of variable descriptions as a JSON string.")] string variables,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(variables, out var parsed, out var error) || parsed is not JsonArray items)
            {
                return ToolResponse.Error(
                    $"variables did not parse as a JSON array: {error ?? "the value is not an array"}",
                    "Expected [{\"name\":\"color/bg\",\"resolvedType\":\"COLOR\",\"valuesByMode\":{\"1:0\":\"#FFF\"}}].");
            }

            var created = new JsonArray();
            var failed = new JsonArray();

            foreach (var item in items)
            {
                var name = item?["name"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(name))
                {
                    failed.Add((JsonNode)new JsonObject { ["item"] = item?.DeepClone(), ["error"] = "the name field is missing" });
                    continue;
                }

                try
                {
                    var result = await Plugin.ApplyAsync(EditorCommands.CreateVariable, new JsonObject
                    {
                        ["name"] = name,
                        ["collectionId"] = collectionId,
                        ["resolvedType"] = (item?["resolvedType"]?.GetValue<string>() ?? "COLOR").ToUpperInvariant(),
                        ["valuesByMode"] = item?["valuesByMode"]?.DeepClone(),
                        ["description"] = item?["description"]?.DeepClone(),
                    }, cancellationToken: ct).ConfigureAwait(false);

                    created.Add(result?.DeepClone() ?? new JsonObject { ["name"] = name });
                }
                catch (EditorCommandException ex)
                {
                    failed.Add((JsonNode)new JsonObject { ["name"] = name, ["error"] = ex.Message });
                }
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["created"] = created,
                ["failed"] = failed,
                ["summary"] = $"{created.Count} created, {failed.Count} failed",
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_batch_update_variables", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.BatchUpdateVariables)]
    public Task<string> BatchUpdateVariablesAsync(
        [Description("Array of updates as a JSON string.")] string updates,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            if (!TryParseJson(updates, out var parsed, out var error) || parsed is not JsonArray items)
            {
                return ToolResponse.Error(
                    $"updates did not parse as a JSON array: {error ?? "the value is not an array"}",
                    "Expected [{\"variableId\":\"…\",\"modeId\":\"1:0\",\"value\":\"#FF0000\"}].");
            }

            var updated = new JsonArray();
            var failed = new JsonArray();

            foreach (var item in items)
            {
                try
                {
                    var result = await Plugin.ApplyAsync(EditorCommands.UpdateVariable, new JsonObject
                    {
                        ["variableId"] = item?["variableId"]?.DeepClone(),
                        ["modeId"] = item?["modeId"]?.DeepClone(),
                        ["value"] = item?["value"]?.DeepClone(),
                    }, cancellationToken: ct).ConfigureAwait(false);

                    updated.Add(result?.DeepClone() ?? item?.DeepClone());
                }
                catch (EditorCommandException ex)
                {
                    failed.Add((JsonNode)new JsonObject { ["item"] = item?.DeepClone(), ["error"] = ex.Message });
                }
            }

            return ToolResponse.Ok(new JsonObject
            {
                ["updated"] = updated,
                ["failed"] = failed,
                ["summary"] = $"{updated.Count} updated, {failed.Count} failed",
            });
        }, cancellationToken);

    [McpServerTool(Name = "figma_setup_design_tokens", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(ToolDescriptions.Variable.SetupDesignTokens)]
    public Task<string> SetupDesignTokensAsync(
        [Description("Collection name, for example 'Design Tokens'.")] string collectionName,
        [Description("Array of variable descriptions as a JSON string.")] string tokens,
        [Description("Name of the first mode, for example 'Light'.")] string? initialModeName = null,
        [Description("Additional modes, comma-separated, for example 'Dark'.")] string? additionalModes = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var collection = await Plugin.ApplyAsync(EditorCommands.CreateVariableCollection, new JsonObject
            {
                ["name"] = collectionName,
                ["initialModeName"] = initialModeName,
                ["additionalModes"] = SplitList(additionalModes),
            }, cancellationToken: ct).ConfigureAwait(false);

            var collectionId = collection?["collectionId"]?.GetValue<string>()
                               ?? collection?["id"]?.GetValue<string>();

            if (collectionId is null)
            {
                return ToolResponse.Error(
                    "The plugin returned no id for the created collection.",
                    "Create the collection with figma_create_variable_collection and pass its id to figma_batch_create_variables.");
            }

            var batch = await BatchCreateVariablesAsync(collectionId, tokens, ct).ConfigureAwait(false);
            var batchNode = JsonNode.Parse(batch);

            return ToolResponse.Ok(new JsonObject
            {
                ["collection"] = collection?.DeepClone(),
                ["collectionId"] = collectionId,
                ["variables"] = batchNode?["data"]?.DeepClone(),
            });
        }, cancellationToken);

    /// <summary>Comma-separated string → JSON array; empty → <c>null</c> so the plugin sees "not set".</summary>
    private static JsonArray? SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var items = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return items.Length == 0 ? null : [.. items.Select(i => (JsonNode)JsonValue.Create(i)!)];
    }

    /// <summary>Variable values arrive as strings; numbers and booleans are recognized so the plugin does not get "12" instead of 12.</summary>
    private static JsonNode? ParseScalar(string value)
    {
        if (bool.TryParse(value, out var flag))
        {
            return JsonValue.Create(flag);
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        if (value.StartsWith('{') || value.StartsWith('['))
        {
            try
            {
                return JsonNode.Parse(value);
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        return JsonValue.Create(value);
    }
}
