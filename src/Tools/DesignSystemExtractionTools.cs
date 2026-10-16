using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Snail.MCP.Figma.Application.Extraction.Codebase;
using Snail.MCP.Figma.Tools.Prompts;

namespace Snail.MCP.Figma.Tools;

/// <summary>Design-system extraction from a codebase (<c>figma_ds_*</c>): analyze → extract tokens → scaffold → extract components → status → verify; Figma joins only at token import.</summary>
[McpServerToolType]
public sealed class DesignSystemExtractionTools(DesignSystemExtraction extraction, ILogger<DesignSystemExtractionTools> logger)
{
    /// <summary>Analyzes one or more application codebases and writes the component and styling inventory that extraction starts from.</summary>
    [McpServerTool(Name = "figma_ds_analyze", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsAnalyze)]
    public Task<string> AnalyzeAsync(
        [Description("Application roots, comma-separated or as a JSON array. Absolute paths are safer: the server working directory is not your project.")] string targets,
        [Description("Where the design system package will be generated. Defaults to '<first target>/design-system'.")] string? outDir = null,
        [Description("Substrings of relative paths: when set, only matching files are scanned.")] string? include = null,
        [Description("Substrings of relative paths to skip. node_modules, dist, .next and the like are always skipped.")] string? exclude = null,
        [Description("Cap on the number of files per target.")] int maxFiles = 5000,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.Analyze(ParseList(targets), outDir, ParseList(include), ParseList(exclude), maxFiles)), cancellationToken);

    /// <summary>Mines design tokens from an analyzed codebase into DTCG and optional code formats.</summary>
    [McpServerTool(Name = "figma_ds_extract_tokens", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsExtractTokens)]
    public Task<string> ExtractTokensAsync(
        [Description("The directory used in figma_ds_analyze: .extraction/analysis.json is read from it.")] string? outDir = null,
        [Description("Scan these roots instead of the targets from the manifest. Rarely needed.")] string? targets = null,
        [Description("Comma-separated file formats under <outDir>/tokens/. dtcg is always written.")] string formats = "dtcg,css-vars",
        [Description("How many times a raw value must occur to become a token.")] int minFrequency = 4,
        [Description("Write files to disk. false is a dry run and returns the document in the response.")] bool write = true,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.ExtractTokens(outDir, ParseList(targets), ParseList(formats), minFrequency, write)), cancellationToken);

    /// <summary>Generates the skeleton of the design system package in the output folder from the analysis and the tokens.</summary>
    [McpServerTool(Name = "figma_ds_scaffold", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsScaffold)]
    public Task<string> ScaffoldAsync(
        [Description("The directory used in figma_ds_analyze and figma_ds_extract_tokens.")] string outDir,
        [Description("npm package name of the design system, for example '@acme/design-system'.")] string? packageName = null,
        [Description("Scaffold framework. Defaults to the first one the analysis found. Storybook has no .astro renderer: for astro the workshop is built on react-vite.")] string? framework = null,
        [Description("Comma-separated token file formats.")] string formats = "dtcg,css-vars",
        [Description("Overwrite scaffold files that already exist.")] bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.Scaffold(outDir, packageName, framework, ParseList(formats), force)), cancellationToken);

    /// <summary>Connects a freshly installed Storybook in the output folder to the extracted tokens, fonts and themes.</summary>
    [McpServerTool(Name = "figma_ds_setup_storybook", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsSetupStorybook)]
    public Task<string> SetupStorybookAsync(
        [Description("Directory of the design system package with a freshly installed .storybook.")] string outDir,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.SetupStorybook(outDir)), cancellationToken);

    /// <summary>Breaks one inventory component down for porting, with its sources, props, variants and a story scaffold.</summary>
    [McpServerTool(Name = "figma_ds_extract_component", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsExtractComponent)]
    public Task<string> ExtractComponentAsync(
        [Description("The directory used in figma_ds_analyze.")] string outDir,
        [Description("Component name from the inventory: see topByUsage in the figma_ds_analyze response or the manifest itself.")] string component,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.ExtractComponent(outDir, component)), cancellationToken);

    /// <summary>Runs the deterministic checks on the extracted package before handover or import into Figma.</summary>
    [McpServerTool(Name = "figma_ds_verify", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsVerify)]
    public Task<string> VerifyAsync(
        [Description("Design system package directory.")] string outDir,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.Verify(outDir)), cancellationToken);

    /// <summary>Reads the summary of the porting progress or records the status of one component.</summary>
    [McpServerTool(Name = "figma_ds_status", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.DesignSystemExtraction.DsStatus)]
    public Task<string> StatusAsync(
        [Description("The directory used in figma_ds_analyze.")] string outDir,
        [Description("Component name from the inventory. Empty just reads the progress.")] string? component = null,
        [Description("New status: pending, in-progress, ported, skipped.")] string? status = null,
        [Description("Why it was skipped, what was merged, what to watch out for.")] string? notes = null,
        [Description("Relative path of the story file once it is written.")] string? storyFile = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => Answer(extraction.Status(outDir, component, status, notes, storyFile)), cancellationToken);

    private static string Answer(ExtractionAnswer answer) =>
        answer.Payload is { } payload ? ToolResponse.Ok(payload) : ToolResponse.Error(answer.Error!, answer.Hint);

    /// <summary>File-system errors come back as data so the model can read the cause and fix the call.</summary>
    private Task<string> RunAsync(Func<string> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Task.FromResult(action());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.ExtractionFileSystemError(logger, ex);

            return Task.FromResult(ToolResponse.Error(
                ex.Message,
                "Check the directory permissions and that the path points at an existing project."));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or RegexMatchTimeoutException)
        {
            Log.ExtractionFailed(logger, ex);

            return Task.FromResult(ToolResponse.Error(ex.Message));
        }
    }

    /// <summary>List from a JSON array or a comma/newline separated string.</summary>
    private static List<string> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith('['))
        {
            try
            {
                if (JsonNode.Parse(trimmed) is JsonArray array)
                {
                    return [.. array
                        .Select(item => item?.GetValue<string>())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Select(item => item!.Trim())];
                }
            }
            catch (JsonException)
            {
            }
        }

        return [.. trimmed.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
