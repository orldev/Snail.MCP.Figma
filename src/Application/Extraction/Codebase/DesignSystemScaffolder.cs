using System.Text.Json;
using System.Text.RegularExpressions;
using Snail.MCP.Figma.Application.Tokens;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Settings for generating the design-system package.</summary>
public sealed record ScaffoldOptions
{
    public required string OutDir { get; init; }

    public required string PackageName { get; init; }

    public required string Framework { get; init; }

    /// <summary>Token file formats; canonical DTCG is always added.</summary>
    public IReadOnlyList<string> Formats { get; init; } = ["dtcg", "css-vars"];

    /// <summary>Overwrite existing files; token files are always refreshed.</summary>
    public bool Force { get; init; }
}

public sealed class ScaffoldResult
{
    public List<string> FilesWritten { get; } = [];

    public List<string> FilesSkipped { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> NextSteps { get; } = [];
}

/// <summary>Scaffolds the <c>design-system/</c> package: skeleton, token files, showcase docs and CSF3 story stubs; Storybook itself is installed by its own CLI.</summary>
public static partial class DesignSystemScaffolder
{
    [GeneratedRegex(@"[^\w]")]
    private static partial Regex NonWordPattern();

    [GeneratedRegex(@"([a-z])([A-Z])")]
    private static partial Regex CamelBoundaryPattern();

    [GeneratedRegex(@"^(variant|kind|type|appearance|intent|color|size)$", RegexOptions.IgnoreCase)]
    private static partial Regex VariantPropPattern();

    /// <summary>Package peer dependencies by application framework.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> FrameworkPeers = new(StringComparer.Ordinal)
    {
        [DsFrameworks.React] = new(StringComparer.Ordinal) { ["react"] = ">=18", ["react-dom"] = ">=18" },
        [DsFrameworks.Next] = new(StringComparer.Ordinal) { ["react"] = ">=18", ["react-dom"] = ">=18" },

        [DsFrameworks.Astro] = new(StringComparer.Ordinal),
        [DsFrameworks.Angular] = new(StringComparer.Ordinal) { ["@angular/core"] = ">=17", ["@angular/common"] = ">=17" },
        [DsFrameworks.WebComponents] = new(StringComparer.Ordinal) { ["lit"] = ">=3" },
        [DsFrameworks.Vue] = new(StringComparer.Ordinal) { ["vue"] = ">=3" },
        [DsFrameworks.Svelte] = new(StringComparer.Ordinal) { ["svelte"] = ">=4" },
        [DsFrameworks.Unknown] = new(StringComparer.Ordinal),
    };

    /// <summary>Dev dependencies installed up front so <c>npm create storybook@latest</c> detects the renderer and builder by itself.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> FrameworkDevDependencies = new(StringComparer.Ordinal)
    {
        [DsFrameworks.React] = new(StringComparer.Ordinal) { ["react"] = "^18.3.0", ["react-dom"] = "^18.3.0", ["vite"] = "^7.0.0" },
        [DsFrameworks.Next] = new(StringComparer.Ordinal) { ["react"] = "^18.3.0", ["react-dom"] = "^18.3.0", ["vite"] = "^7.0.0" },
        [DsFrameworks.Astro] = new(StringComparer.Ordinal) { ["react"] = "^18.3.0", ["react-dom"] = "^18.3.0", ["vite"] = "^7.0.0" },

        [DsFrameworks.Angular] = new(StringComparer.Ordinal),
        [DsFrameworks.WebComponents] = new(StringComparer.Ordinal) { ["lit"] = "^3.0.0", ["vite"] = "^7.0.0" },
        [DsFrameworks.Vue] = new(StringComparer.Ordinal) { ["vue"] = "^3.4.0", ["vite"] = "^7.0.0" },
        [DsFrameworks.Svelte] = new(StringComparer.Ordinal) { ["svelte"] = "^4.0.0", ["vite"] = "^7.0.0" },
        [DsFrameworks.Unknown] = new(StringComparer.Ordinal) { ["vite"] = "^7.0.0" },
    };

    public static ScaffoldResult Scaffold(DsAnalysis analysis, TokenDocument document, ScaffoldOptions options)
    {
        var result = new ScaffoldResult();
        var formats = TokenFileWriter.WithCanonicalFirst(options.Formats);

        WritePackageJson(analysis, options, result);

        var tokenFiles = TokenFileWriter.Write(document, options.OutDir, formats);
        result.FilesWritten.AddRange(tokenFiles.FilesWritten.Select(path => Relative(options.OutDir, path)));
        result.Warnings.AddRange(tokenFiles.Warnings);

        WriteAggregateCss(options, formats, result);

        Write(options, result, "src/components/README.md",
            "# Components\n\nOne directory per component: `ComponentName/ComponentName.{ext}`, `ComponentName.stories.{ext}`, `index.{ext}`.\n\n"
            + "Ported from the source applications by the design-system extraction process — the inventory lives in `.extraction/analysis.json`, "
            + "progress in `.extraction/status.json`.\n");

        Write(options, result, "src/docs/Tokens.mdx", ShowcaseMdx.TokenShowcase(document), alwaysOverwrite: true);

        var typographyRules = analysis.Targets.SelectMany(t => t.Typography).ToList();
        Write(options, result, "src/docs/Typography.mdx",
            ShowcaseMdx.TypographyShowcase(document, typographyRules), alwaysOverwrite: true);

        var icons = analysis.Targets
            .SelectMany(target => target.Icons.Select(icon => (Icon: icon, target.Root)))
            .ToList();

        if (icons.Count > 0)
        {
            Write(options, result, "src/docs/Icons.mdx", ShowcaseMdx.IconShowcase(icons), alwaysOverwrite: true);
        }

        Write(options, result, "README.md", GenerateReadme(analysis, options, formats));

        result.NextSteps.Add(
            $"cd {options.OutDir} && npm install && npm create storybook@latest -- --yes --no-dev "
            + $"(the CLI will detect {options.Framework} and vite on its own from the devDependencies installed beforehand)");
        result.NextSteps.Add(
            "After initialisation, import \"../tokens/index.css\" (or tokens.css) in .storybook/preview so the tokens load in every story.");
        result.NextSteps.Add(
            "Check that .storybook/main has the src/**/*.mdx glob — otherwise the token showcase will not appear.");
        result.NextSteps.Add(
            "Port the components in rank order: figma_ds_extract_component for each, then figma_ds_status to record progress.");

        return result;
    }

    /// <summary>CSF3 story stub for an inventory entry.</summary>
    public static string StoryScaffold(DsComponentEntry component)
    {
        var name = component.Name;
        var variant = component.ObservedProps?
            .FirstOrDefault(pair => pair.Value.Count > 1 && VariantPropPattern().IsMatch(pair.Key));

        if (component.Framework == DsFrameworks.WebComponents)
        {
            var tag = CamelBoundaryPattern().Replace(name, "$1-$2").ToLowerInvariant();

            return string.Join('\n',
            [
                "import type { Meta, StoryObj } from '@storybook/web-components-vite';",
                "import { html } from 'lit';",
                $"import './{name}';",
                string.Empty,
                "const meta: Meta = {",
                $"  title: 'Components/{name}',",
                $"  component: '{tag}',",
                "  tags: ['autodocs'],",
                "};",
                "export default meta;",
                "type Story = StoryObj;",
                string.Empty,
                "export const Default: Story = {",
                $"  render: () => html`<{tag}></{tag}>`,",
                "};",
                string.Empty,
            ]);
        }

        if (component.Framework == DsFrameworks.Angular)
        {
            return string.Join('\n',
            [
                "import type { Meta, StoryObj } from '@storybook/angular';",
                $"import {{ {name} }} from './{name.ToLowerInvariant()}.component';",
                string.Empty,
                $"const meta: Meta<{name}> = {{",
                $"  title: 'Components/{name}',",
                $"  component: {name},",
                "  tags: ['autodocs'],",
                "};",
                "export default meta;",
                $"type Story = StoryObj<{name}>;",
                string.Empty,
                "export const Default: Story = {};",
                string.Empty,
            ]);
        }

        if (component.Framework == DsFrameworks.Astro)
        {
            return string.Join('\n',
            [
                $"// Ported from {component.File ?? "an .astro component"} — markup reproduced in JSX.",
                "import type { Meta, StoryObj } from '@storybook/react-vite';",
                $"import {{ {name} }} from './{name}';",
                string.Empty,
                "const meta = {",
                $"  title: 'Components/{name}',",
                $"  component: {name},",
                "  tags: ['autodocs'],",
                $"}} satisfies Meta<typeof {name}>;",
                "export default meta;",
                "type Story = StoryObj<typeof meta>;",
                string.Empty,
                "export const Default: Story = { args: {} };",
                string.Empty,
            ]);
        }

        var lines = new List<string>
        {
            "import type { Meta, StoryObj } from '@storybook/react-vite';",
            $"import {{ {name} }} from './{name}';",
            string.Empty,
            "const meta = {",
            $"  title: 'Components/{name}',",
            $"  component: {name},",
            "  tags: ['autodocs'],",
            $"}} satisfies Meta<typeof {name}>;",
            "export default meta;",
            "type Story = StoryObj<typeof meta>;",
            string.Empty,
            "export const Default: Story = { args: {} };",
        };

        if (variant is { } entry && entry.Value is not null)
        {
            var used = new HashSet<string>(StringComparer.Ordinal) { "Default" };

            foreach (var value in entry.Value.Take(8))
            {
                var storyName = string.Concat(NonWordPattern()
                    .Replace(value, " ")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

                if (storyName.Length == 0 || char.IsDigit(storyName[0]))
                {
                    storyName = $"Variant{storyName}";
                }

                while (!used.Add(storyName))
                {
                    storyName = $"{storyName}Variant";
                }

                lines.Add(string.Empty);
                lines.Add($"export const {storyName}: Story = {{");
                lines.Add($"  args: {{ {entry.Key}: {JsonSerializer.Serialize(value)} }},");
                lines.Add("};");
            }
        }

        lines.Add(string.Empty);

        return string.Join('\n', lines);
    }

    private static void WritePackageJson(DsAnalysis analysis, ScaffoldOptions options, ScaffoldResult result)
    {
        var devDependencies = new JsonObject();

        foreach (var (name, version) in FrameworkDevDependencies.GetValueOrDefault(options.Framework, []))
        {
            devDependencies[name] = version;
        }

        if (analysis.Targets.Any(t => t.StylingMethods.Any(s => s.StartsWith("tailwind", StringComparison.Ordinal))))
        {
            devDependencies["tailwindcss"] = "^4.0.0";
            devDependencies["@tailwindcss/vite"] = "^4.0.0";
        }

        var peers = new JsonObject();

        foreach (var (name, version) in FrameworkPeers.GetValueOrDefault(options.Framework, []))
        {
            peers[name] = version;
        }

        var package = new JsonObject
        {
            ["name"] = options.PackageName,
            ["version"] = "0.1.0",
            ["private"] = true,
            ["type"] = "module",
            ["description"] =
                "Design system extracted from production applications by the snail-figma server. "
                + "Components live in src/components, tokens in tokens/.",
            ["files"] = new JsonArray("dist", "tokens"),
            ["peerDependencies"] = peers,
            ["devDependencies"] = devDependencies,

            ["scripts"] = new JsonObject(),
        };

        Write(options, result, "package.json", package.ToJsonString(DsJson.Options) + '\n');
    }

    private static void WriteAggregateCss(ScaffoldOptions options, IReadOnlyList<string> formats, ScaffoldResult result)
    {
        var hasCssVars = formats.Contains("css-vars", StringComparer.OrdinalIgnoreCase);
        var hasTailwind = formats.Contains("tailwind-v4", StringComparer.OrdinalIgnoreCase);

        if (!hasCssVars && !hasTailwind)
        {
            return;
        }

        var imports = new List<string>();

        if (hasCssVars) imports.Add("@import \"./tokens.css\";");
        if (hasTailwind) imports.Add("@import \"./theme.css\";");

        Write(options, result, "tokens/index.css", string.Join('\n', imports) + '\n');
    }

    private static void Write(
        ScaffoldOptions options,
        ScaffoldResult result,
        string relativePath,
        string content,
        bool alwaysOverwrite = false)
    {
        var absolute = Path.Combine(options.OutDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(absolute) && !options.Force && !alwaysOverwrite)
        {
            result.FilesSkipped.Add(relativePath);

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
        result.FilesWritten.Add(relativePath);
    }

    private static string Relative(string outDir, string absolute) =>
        Path.GetRelativePath(outDir, absolute).Replace(Path.DirectorySeparatorChar, '/');

    private static string GenerateReadme(DsAnalysis analysis, ScaffoldOptions options, IReadOnlyList<string> formats)
    {
        var targets = string.Join('\n', analysis.Targets.Select(target =>
        {
            var styling = target.StylingMethods.Count > 0
                ? string.Join(", ", target.StylingMethods)
                : "the styling approach could not be determined";

            return $"- `{target.Root}` ({string.Join(", ", target.Frameworks)}; {styling})";
        }));

        var portable = analysis.Components.Count(c =>
            !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal));

        return $"""
        # {options.PackageName}

        Design system extracted from production applications by the `figma_ds_*` process of the snail-figma server
        (a port of [figma-console-mcp](https://github.com/southleft/figma-console-mcp), MIT, Southleft).

        ## Source applications

        {targets}

        ## What is inside

        - `tokens/` — design tokens. `tokens.json` is the canonical DTCG with the provenance of every
          token in `$extensions`; the other files are derived projections ({string.Join(", ", formats)}).
          Push them into Figma with `figma_import_tokens`.
        - `src/components/` — components ported from the source applications ({portable} in the inventory;
          ranking in `.extraction/analysis.json`, progress in `.extraction/status.json`).
        - `src/docs/Tokens.mdx` — token showcase, the bird's-eye view in Storybook.

        ## Getting started

        ```bash
        npm create storybook@latest   # detects {options.Framework} and installs the current Storybook
        ```

        Then import `tokens/index.css` (or `tokens/tokens.css`) in `.storybook/preview`
        so every story renders with the mined tokens.

        ## Living with it

        This package is the source of truth for the UI:

        1. **Code leads**: evolve components here, version and publish the package, consume it from the apps.
        2. **Design leads**: additionally push `tokens/tokens.json` into Figma variables
           (`figma_import_tokens`) and rebuild the key components as Figma component sets.

        """;
    }
}
