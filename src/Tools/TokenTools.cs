using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Documents;
using Snail.MCP.Figma.Application.Editor;
using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;
using Snail.MCP.Figma.Application.Tokens.Parsers;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Token exchange between Figma and code: export variables into a format, import code files back into Figma.</summary>
[McpServerToolType]
public sealed class TokenTools(
    ILiveEditor plugin,
    IPublishedDocumentSource api,
    ActiveDocumentTracker activeFile,
    ILogger<TokenTools> logger)
    : EditorToolBase(plugin, logger)
{
    /// <summary>Exports the variables of the file as token files in a chosen code format.</summary>
    [McpServerTool(Name = "figma_export_tokens", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description(ToolDescriptions.Token.ExportTokens)]
    public Task<string> ExportTokensAsync(
        [Description("Output format.")] string format = "dtcg",
        [Description("Link to the Figma file. Optional if the file has already been used.")] string? fileUrl = null,
        [Description("Prefix of the token names, for example 'ds-'.")] string? prefix = null,
        [Description("A separate file per mode.")] bool splitByMode = false,
        [Description("A separate file per collection.")] bool splitByCollection = false,
        [Description("Unfold references into final values.")] bool resolveAliases = false,
        [Description("Color format: hex, hex8, rgba, hsl, oklch.")] string colorFormat = "hex",
        [Description("Size unit: px, rem, pt, dp.")] string? sizeUnit = null,
        [Description("Base font size for converting px into rem.")] double remBase = 16,
        [Description("Mode renaming, JSON of the form {\"Light\":\"light\",\"Dark\":\"dark\"}.")] string? modeMap = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var formatter = TokenFormatters.Find(format);

            if (formatter is null)
            {
                return UnknownFormat(format, TokenFormatters.Formats);
            }

            if (!TryParseJson(modeMap, out var parsedModeMap, out var modeMapError))
            {
                return ToolResponse.Error($"modeMap did not parse as JSON: {modeMapError}");
            }

            var (variables, source) = await LoadVariablesAsync(fileUrl, ct).ConfigureAwait(false);

            if (variables is null)
            {
                return ToolResponse.Error(
                    "The variables could not be fetched.",
                    "Run the Snail Bridge plugin — through it variables are read on any plan.");
            }

            var document = FigmaVariableConverter.Convert(variables, ReadModeMap(parsedModeMap));

            var options = new FormatOptions
            {
                Prefix = prefix ?? string.Empty,
                ShouldSplitByMode = splitByMode,
                ShouldSplitByCollection = splitByCollection,
                ShouldResolveAliases = resolveAliases || !TokenFormatters.SupportsAliases(formatter.Format),
                ColorFormat = colorFormat,
                SizeUnit = sizeUnit,
                RemBase = remBase,
            };

            var result = formatter.Write(document, options);

            var files = new JsonArray(result.Files
                .Select(file => (JsonNode)new JsonObject
                {
                    ["path"] = file.Path,
                    ["content"] = file.Content,
                })
                .ToArray());

            return ToolResponse.Ok(new JsonObject
            {
                ["format"] = formatter.Format,
                ["source"] = source,
                ["sets"] = new JsonArray(document.Sets
                    .Select(set => (JsonNode)new JsonObject
                    {
                        ["name"] = set.Name,
                        ["modes"] = ToJsonArray(set.Modes),
                        ["tokens"] = set.Tokens.Count,
                    })
                    .ToArray()),
                ["tokenCount"] = document.TokenCount,
                ["files"] = files,
                ["warnings"] = ToJsonArray(result.Warnings),
            });
        }, cancellationToken);

    /// <summary>Parses tokens from code and either plans or applies them as Figma variables.</summary>
    [McpServerTool(Name = "figma_import_tokens", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Token.ImportTokens)]
    public Task<string> ImportTokensAsync(
        [Description("Contents of the token file.")] string content,
        [Description("Source format. Empty means detect it from the file name.")] string? format = null,
        [Description("File name — the format is guessed from it.")] string? fileName = null,
        [Description("Name of the Figma collection to push the tokens into.")] string? collectionName = null,
        [Description("Only show the plan, change nothing.")] bool dryRun = true,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async ct =>
        {
            var effectiveFormat = format ?? (fileName is not null ? TokenParsers.GuessFormat(fileName) : "dtcg");
            var parser = TokenParsers.Find(effectiveFormat);

            if (parser is null)
            {
                return UnknownFormat(effectiveFormat, TokenParsers.Formats);
            }

            var setName = collectionName
                          ?? (fileName is null ? "Tokens" : Path.GetFileNameWithoutExtension(fileName));

            var parsed = parser.Parse(content, new ParseContext(setName, new TokenDocument()));

            if (parsed.TokenCount == 0)
            {
                return ToolResponse.Error(
                    $"Not a single token could be read from the contents (format {effectiveFormat}).",
                    string.Join(" ", parsed.Warnings.DefaultIfEmpty("Check the format and the file contents.")));
            }

            var plan = BuildPlan(parsed.Document);

            if (dryRun)
            {
                return ToolResponse.Ok(new JsonObject
                {
                    ["dryRun"] = true,
                    ["format"] = parser.Format,
                    ["plan"] = plan,
                    ["warnings"] = ToJsonArray(parsed.Warnings),
                    ["note"] = "Nothing was changed. Repeat with dryRun=false to apply.",
                });
            }

            var applied = await ApplyAsync(parsed.Document, ct).ConfigureAwait(false);

            return ToolResponse.Ok(new JsonObject
            {
                ["dryRun"] = false,
                ["format"] = parser.Format,
                ["applied"] = applied,
                ["warnings"] = ToJsonArray(parsed.Warnings),
            });
        }, cancellationToken);

    /// <summary>Converts token content from one code format to another without touching Figma.</summary>
    [McpServerTool(Name = "figma_convert_tokens", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Token.ConvertTokens)]
    public Task<string> ConvertTokensAsync(
        [Description("Contents of the source file.")] string content,
        [Description("Source format.")] string fromFormat,
        [Description("Target format.")] string toFormat,
        [Description("Prefix of the token names.")] string? prefix = null,
        [Description("Color format: hex, hex8, rgba, hsl, oklch.")] string colorFormat = "hex",
        [Description("Size unit: px, rem, pt, dp.")] string? sizeUnit = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ =>
        {
            var parser = TokenParsers.Find(fromFormat);

            if (parser is null)
            {
                return Task.FromResult(UnknownFormat(fromFormat, TokenParsers.Formats));
            }

            var formatter = TokenFormatters.Find(toFormat);

            if (formatter is null)
            {
                return Task.FromResult(UnknownFormat(toFormat, TokenFormatters.Formats));
            }

            var parsed = parser.Parse(content, new ParseContext("Tokens", new TokenDocument()));

            var result = formatter.Write(parsed.Document, new FormatOptions
            {
                Prefix = prefix ?? string.Empty,
                ShouldResolveAliases = !TokenFormatters.SupportsAliases(formatter.Format),
                ColorFormat = colorFormat,
                SizeUnit = sizeUnit,
            });

            return Task.FromResult(ToolResponse.Ok(new JsonObject
            {
                ["from"] = parser.Format,
                ["to"] = formatter.Format,
                ["tokenCount"] = parsed.TokenCount,
                ["files"] = new JsonArray(result.Files
                    .Select(file => (JsonNode)new JsonObject
                    {
                        ["path"] = file.Path,
                        ["content"] = file.Content,
                    })
                    .ToArray()),
                ["warnings"] = ToJsonArray([.. parsed.Warnings, .. result.Warnings]),
            }));
        }, cancellationToken);

    /// <summary>Variables from the plugin, or from REST when it is absent; the plugin works on any plan, so it goes first.</summary>
    private async Task<(JsonNode? Variables, string Source)> LoadVariablesAsync(
        string? fileUrl,
        CancellationToken cancellationToken)
    {
        if (Plugin.IsConnected)
        {
            try
            {
                var fromPlugin = await Plugin
                    .ReadAsync(EditorCommands.GetVariablesData, timeout: TimeSpan.FromSeconds(45),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (fromPlugin is JsonObject { Count: > 0 })
                {
                    return (fromPlugin, "plugin");
                }
            }
            catch (Exception ex) when (ex is EditorCommandException or TimeoutException or EditorUnavailableException)
            {
                Log.VariablesTriedThroughRest(Logger, ex.Message);
            }
        }

        try
        {
            var fileKey = activeFile.Resolve(fileUrl);

            return (await api.GetLocalVariablesAsync(fileKey, cancellationToken).ConfigureAwait(false), "figma-api");
        }
        catch (PublishedSourceException ex)
        {
            Log.VariablesRestUnavailable(Logger, ex.Message);

            return (null, "unavailable");
        }
    }

    /// <summary>Import plan: which collections, modes and tokens will go into Figma.</summary>
    private static JsonObject BuildPlan(TokenDocument document) => new()
    {
        ["tokenCount"] = document.TokenCount,
        ["sets"] = new JsonArray(document.Sets
            .Select(set => (JsonNode)new JsonObject
            {
                ["name"] = set.Name,
                ["modes"] = ToJsonArray(set.Modes),
                ["tokens"] = new JsonArray(set.Tokens
                    .Take(50)
                    .Select(token => (JsonNode)new JsonObject
                    {
                        ["path"] = token.SlashPath,
                        ["type"] = token.Type,
                        ["values"] = ValuesByMode(token),
                    })
                    .ToArray()),
                ["truncated"] = set.Tokens.Count > 50,
            })
            .ToArray()),
    };

    /// <summary>Pushes tokens into Figma, creating collections, modes and variables through the plugin.</summary>
    private async Task<JsonObject> ApplyAsync(TokenDocument document, CancellationToken cancellationToken)
    {
        var created = 0;
        var failed = new JsonArray();

        foreach (var set in document.Sets)
        {
            var collection = await Plugin.ApplyAsync(EditorCommands.CreateVariableCollection, new JsonObject
            {
                ["name"] = set.Name,
                ["initialModeName"] = set.Modes.FirstOrDefault(),
                ["additionalModes"] = new JsonArray(set.Modes.Skip(1)
                    .Select(mode => (JsonNode)JsonValue.Create(mode)!).ToArray()),
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            var collectionId = collection?["collectionId"]?.GetValue<string>() ?? collection?["id"]?.GetValue<string>();

            if (collectionId is null)
            {
                failed.Add((JsonNode)new JsonObject { ["set"] = set.Name, ["error"] = "the plugin returned no collection id" });
                continue;
            }

            foreach (var token in set.Tokens)
            {
                try
                {
                    await Plugin.ApplyAsync(EditorCommands.CreateVariable, new JsonObject
                    {
                        ["name"] = token.SlashPath,
                        ["collectionId"] = collectionId,
                        ["resolvedType"] = ToFigmaType(token.Type),
                        ["description"] = token.Description,
                        ["valuesByMode"] = new JsonObject(token.Values
                            .Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value.AsText())))),
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);

                    created++;
                }
                catch (EditorCommandException ex)
                {
                    failed.Add((JsonNode)new JsonObject { ["token"] = token.SlashPath, ["error"] = ex.Message });
                }
            }
        }

        return new JsonObject
        {
            ["created"] = created,
            ["failed"] = failed,
            ["summary"] = $"{created} variables created, {failed.Count} failed",
        };
    }

    /// <summary>DTCG type → native Figma variable type.</summary>
    internal static string ToFigmaType(string type) => type switch
    {
        TokenTypes.Color => "COLOR",
        TokenTypes.Dimension or TokenTypes.Number => "FLOAT",
        TokenTypes.Boolean => "BOOLEAN",
        _ => "STRING",
    };

    /// <summary>Mode rename map from the tool parameter.</summary>
    private static IReadOnlyDictionary<string, string>? ReadModeMap(JsonNode? modeMap)
    {
        if (modeMap is not JsonObject obj || obj.Count == 0)
        {
            return null;
        }

        return obj.ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<string>() ?? pair.Key);
    }

    private static string UnknownFormat(string requested, IReadOnlyList<string> supported) =>
        ToolResponse.Error(
            $"Format '{requested}' is not supported.",
            $"Available: {string.Join(", ", supported)}");

    private static JsonArray ToJsonArray(IReadOnlyList<string> values) =>
        [.. values.Select(value => (JsonNode)JsonValue.Create(value)!)];

    /// <summary>A token's value in each mode, as the plan shows it.</summary>
    private static JsonObject ValuesByMode(Token token) =>
        new(token.Values.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value.AsText()))));
}
