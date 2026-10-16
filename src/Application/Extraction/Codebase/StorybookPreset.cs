using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>What setting up the Storybook glue wrote, patched and still leaves to the caller.</summary>
public sealed class StorybookPresetResult
{
    /// <summary>Paths of the files written, relative to the output directory.</summary>
    public List<string> FilesWritten { get; } = [];

    /// <summary>Paths of existing Storybook files that were patched in place, relative to the output directory.</summary>
    public List<string> Patched { get; } = [];

    /// <summary>Changes the setup could not make on its own and the caller has to make by hand.</summary>
    public List<string> ManualSteps { get; } = [];

    /// <summary>Problems met during setup that did not stop it.</summary>
    public List<string> Warnings { get; } = [];
}

/// <summary>Storybook workshop glue for an extracted design system: <c>preview.css</c>, a <c>main.js</c> patch and a theme decorator in <c>preview.jsx</c>.</summary>
public static partial class StorybookPreset
{
    [GeneratedRegex(@"@theme\b[^{]*")]
    private static partial Regex ThemeBlockMarker();

    [GeneratedRegex(@"@utility\s+[\w-]+\s*")]
    private static partial Regex UtilityBlockMarker();

    [GeneratedRegex(@"@layer base\s*")]
    private static partial Regex BaseLayerMarker();

    [GeneratedRegex(@"@font-face\s*")]
    private static partial Regex FontFaceMarker();

    [GeneratedRegex(@"url\(['""]?(/[^'"")]+)['""]?\)")]
    private static partial Regex FontUrlPattern();

    [GeneratedRegex(@"^\s*@(import|plugin|custom-variant|source)\b[^;]*;\s*$", RegexOptions.Multiline)]
    private static partial Regex FrameworkDirectivePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLineRunPattern();

    [GeneratedRegex(@"[.\w\[][^{}]*\{[^}]*\}")]
    private static partial Regex AnyRulePattern();

    [GeneratedRegex(@"--([\w-]+)\s*:")]
    private static partial Regex DefinedVariablePattern();

    [GeneratedRegex(@"var\(\s*--([\w-]+)\s*\)")]
    private static partial Regex ConsumedVariablePattern();

    [GeneratedRegex(@"font|typeface", RegexOptions.IgnoreCase)]
    private static partial Regex FontVariablePattern();

    [GeneratedRegex(@"mono|code", RegexOptions.IgnoreCase)]
    private static partial Regex MonoVariablePattern();

    [GeneratedRegex(@"(""framework"":\s*""[^""]+""),?")]
    private static partial Regex FrameworkAnchorPattern();

    [GeneratedRegex(@"const preview = \{")]
    private static partial Regex PreviewObjectPattern();

    private const long MaxStylesheetBytes = 200 * 1024;

    /// <summary>All balanced-brace blocks starting with the marker.</summary>
    private static List<string> ExtractBlocks(string css, Regex marker)
    {
        var blocks = new List<string>();
        var searchFrom = 0;

        while (searchFrom < css.Length)
        {
            var match = marker.Match(css, searchFrom);

            if (!match.Success)
            {
                break;
            }

            var start = css.IndexOf('{', match.Index);

            if (start < 0)
            {
                searchFrom = match.Index + Math.Max(1, match.Length);
                continue;
            }

            var depth = 0;
            var end = -1;

            for (var i = start; i < css.Length; i++)
            {
                if (css[i] == '{')
                {
                    depth++;
                }
                else if (css[i] == '}')
                {
                    depth--;

                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            if (end < 0)
            {
                break;
            }

            blocks.Add(css[match.Index..(end + 1)]);
            searchFrom = end + 1;
        }

        return blocks;
    }

    /// <summary>Generates and patches the glue inside <c>&lt;outDir&gt;/.storybook</c>; safe to rerun.</summary>
    public static StorybookPresetResult Setup(DsAnalysis analysis, string outDir)
    {
        var result = new StorybookPresetResult();
        var storybookDir = Path.Combine(outDir, ".storybook");

        if (!Directory.Exists(storybookDir))
        {
            result.ManualSteps.Add(
                "There is no .storybook directory — run `npm create storybook@latest -- --yes --no-dev` in the package first, then run the tool again.");

            return result;
        }

        var target = analysis.Targets.FirstOrDefault();
        var usesTailwind = target?.StylingMethods.Any(s => s.StartsWith("tailwind", StringComparison.Ordinal)) ?? false;

        if (usesTailwind)
        {
            CheckTailwindPackages(outDir, result);
        }

        var mined = MineSourceStyles(target, result);

        CopyFontFiles(target, outDir, mined.FontFiles, result);

        var previewCss = BuildPreviewCss(outDir, usesTailwind, mined, result);
        File.WriteAllText(Path.Combine(storybookDir, "preview.css"), previewCss);
        result.FilesWritten.Add(".storybook/preview.css");

        PatchMain(storybookDir, outDir, usesTailwind, mined.FontFiles.Count > 0, result);
        PatchPreview(storybookDir, outDir, result);

        return result;
    }

    private sealed class MinedStyles
    {
        public List<string> ThemeBlocks { get; } = [];

        public List<string> UtilityBlocks { get; } = [];

        public List<string> BaseBlocks { get; } = [];

        public List<string> FontFaces { get; } = [];

        public List<string> SharedRules { get; } = [];

        public HashSet<string> FontFiles { get; } = new(StringComparer.Ordinal);
    }

    private static void CheckTailwindPackages(string outDir, StorybookPresetResult result)
    {
        var raw = CodeWalker.ReadText(Path.Combine(outDir, "package.json"));

        if (raw is null)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var hasPlugin = new[] { "dependencies", "devDependencies" }.Any(section =>
                root.TryGetProperty(section, out var block)
                && block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("@tailwindcss/vite", out _));

            if (!hasPlugin)
            {
                result.ManualSteps.Add(
                    "Install the Tailwind packages the inserted vite plugin needs: npm install --save-dev tailwindcss @tailwindcss/vite");
            }
        }
        catch (JsonException)
        {
        }
    }

    private static MinedStyles MineSourceStyles(DsTargetAnalysis? target, StorybookPresetResult result)
    {
        var mined = new MinedStyles();

        foreach (var source in target?.StyleSources ?? [])
        {
            if (source.Kind is not ("css" or "scss"))
            {
                continue;
            }

            var absolute = CodeWalker.ResolveUnder(target!.Root, source.File);
            var css = absolute is null ? null : CodeWalker.ReadText(absolute, (int)MaxStylesheetBytes);

            if (css is null || css.Length > MaxStylesheetBytes)
            {
                continue;
            }

            var theme = ExtractBlocks(css, ThemeBlockMarker());
            var utilities = ExtractBlocks(css, UtilityBlockMarker());
            var baseLayer = ExtractBlocks(css, BaseLayerMarker());
            var faces = ExtractBlocks(css, FontFaceMarker());

            mined.ThemeBlocks.AddRange(theme);
            mined.UtilityBlocks.AddRange(utilities);
            mined.BaseBlocks.AddRange(baseLayer);
            mined.FontFaces.AddRange(faces);

            foreach (var face in faces)
            {
                foreach (Match url in FontUrlPattern().Matches(face))
                {
                    mined.FontFiles.Add(url.Groups[1].Value);
                }
            }

            var remainder = theme.Concat(utilities).Concat(baseLayer).Concat(faces)
                .Aggregate(css, (current, block) => current.Replace(block, string.Empty, StringComparison.Ordinal));

            remainder = BlankLineRunPattern()
                .Replace(FrameworkDirectivePattern().Replace(remainder, string.Empty), "\n\n")
                .Trim();

            if (AnyRulePattern().IsMatch(remainder))
            {
                mined.SharedRules.Add($"/* ---- shared rules from {source.File} (verbatim) ---- */\n{remainder}");
            }
        }

        return mined;
    }

    private static void CopyFontFiles(
        DsTargetAnalysis? target,
        string outDir,
        IEnumerable<string> fontFiles,
        StorybookPresetResult result)
    {
        if (target is null)
        {
            return;
        }

        var publicRoot = Path.GetFullPath(Path.Combine(outDir, "public"));

        foreach (var file in fontFiles)
        {
            var relative = file.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(target.Root, "public", relative);
            var destination = Path.Combine(outDir, "public", relative);

            if (!Path.GetFullPath(destination).StartsWith(publicRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                result.Warnings.Add($"Font copy skipped — the path escapes upwards: {file}");
                continue;
            }

            if (!File.Exists(source) || File.Exists(destination))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
            result.FilesWritten.Add($"public{file}");
        }
    }

    private static string BuildPreviewCss(
        string outDir,
        bool usesTailwind,
        MinedStyles mined,
        StorybookPresetResult result)
    {
        var tokensCssPath = Path.Combine(outDir, "tokens", "tokens.css");
        var tokensImport = File.Exists(tokensCssPath)
            ? "@import '../tokens/tokens.css';"
            : "/* tokens/tokens.css not found — run figma_ds_extract_tokens with the css-vars format */";

        var parts = new List<string>
        {
            "/* Generated by figma_ds_setup_storybook — safe to rerun, manual edits are overwritten.",
            "   Order matters: tailwind → dark variant → mined tokens → the source app's @theme utility map,",
            "   its @utility, @layer base and @font-face. */",
            usesTailwind ? "@import 'tailwindcss';" : string.Empty,
            string.Empty,
            usesTailwind
                ? "@custom-variant dark (&:where([data-theme='dark'], [data-theme='dark'] *, .dark, .dark *));"
                : string.Empty,
            string.Empty,
            tokensImport,
            string.Empty,
        };

        parts.AddRange(mined.ThemeBlocks);
        parts.Add(string.Empty);
        parts.AddRange(mined.UtilityBlocks);
        parts.Add(string.Empty);
        parts.AddRange(mined.BaseBlocks);
        parts.Add(string.Empty);
        parts.AddRange(mined.FontFaces);
        parts.Add(string.Empty);
        parts.AddRange(mined.SharedRules);
        parts.AddRange(
        [
            string.Empty,
            "/* Storybook's own stylesheet forces its font (Nunito Sans) onto form elements that do not",
            "   inherit font-family: the app cascade is overridden and every ported button renders in the",
            "   wrong family. Restore the font with story-root priority. */",
            "#storybook-root button, #storybook-root input, #storybook-root textarea, #storybook-root select,",
            ".sb-story button, .sb-story input, .sb-story textarea, .sb-story select {",
            "  font-family: inherit;",
            "}",
            string.Empty,
        ]);

        var lines = new List<string>();

        foreach (var part in parts)
        {
            if (part.Length == 0 && lines.Count > 0 && lines[^1].Length == 0)
            {
                continue;
            }

            lines.Add(part);
        }

        var previewCss = string.Join('\n', lines);

        var tokensCssText = File.Exists(tokensCssPath) ? File.ReadAllText(tokensCssPath) : string.Empty;
        var defined = new HashSet<string>(
            DefinedVariablePattern().Matches(previewCss + tokensCssText).Select(m => m.Groups[1].Value),
            StringComparer.Ordinal);

        var fallbacks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in ConsumedVariablePattern().Matches(previewCss))
        {
            var name = match.Groups[1].Value;

            if (defined.Contains(name) || !seen.Add(name))
            {
                continue;
            }

            if (FontVariablePattern().IsMatch(name))
            {
                var stack = MonoVariablePattern().IsMatch(name)
                    ? "ui-monospace, 'SF Mono', Menlo, monospace"
                    : "system-ui, -apple-system, sans-serif";

                fallbacks.Add($"  --{name}: {stack}; /* injected at runtime (next/font and alike) — workshop fallback */");
                result.ManualSteps.Add(
                    $"--{name} falls back to the system font stack. For exact typography find the real font in the app's "
                    + "next/font (or equivalent) setup, install its @fontsource package, import it in .storybook/preview.jsx "
                    + $"and put it first in the --{name} fallback stack in preview.css.");
            }
            else
            {
                result.Warnings.Add(
                    $"var(--{name}) is consumed but never declared in CSS — most likely it is injected at runtime; if the stories render wrong, add a fallback value in .storybook/preview.css.");
            }
        }

        if (fallbacks.Count > 0)
        {
            previewCss += $"\n/* Variables injected at runtime — workshop fallbacks */\n:root {{\n{string.Join('\n', fallbacks)}\n}}\n";
        }

        return previewCss;
    }

    private static void PatchMain(
        string storybookDir,
        string outDir,
        bool usesTailwind,
        bool hasFonts,
        StorybookPresetResult result)
    {
        var mainPath = new[] { "main.js", "main.ts" }
            .Select(name => Path.Combine(storybookDir, name))
            .FirstOrDefault(File.Exists);

        if (mainPath is null)
        {
            result.ManualSteps.Add(
                "There is no .storybook/main.js|ts to edit — add the tailwind plugin for vite and esbuild.jsx='automatic' by hand.");

            return;
        }

        var main = File.ReadAllText(mainPath);
        var changed = false;

        if (usesTailwind && !main.Contains("@tailwindcss/vite", StringComparison.Ordinal))
        {
            main = $"import tailwindcss from \"@tailwindcss/vite\";\n{main}";
            changed = true;
        }

        if (!main.Contains("viteFinal", StringComparison.Ordinal))
        {
            var inject = new List<string> { "  async viteFinal(viteConfig) {" };

            if (usesTailwind)
            {
                inject.Add("    viteConfig.plugins = [...(viteConfig.plugins ?? []), tailwindcss()];");
            }

            inject.AddRange(
            [
                "    // The package has no vite.config, so react-vite does not add the React plugin;",
                "    // without the automatic JSX runtime stories fail with \"React is not defined\".",
                "    viteConfig.esbuild = { ...viteConfig.esbuild, jsx: \"automatic\" };",
                "    return viteConfig;",
                "  },",
            ]);

            if (FrameworkAnchorPattern().IsMatch(main))
            {
                main = FrameworkAnchorPattern().Replace(main, $"$1,\n{string.Join('\n', inject)}", 1);
                changed = true;
            }
            else
            {
                result.ManualSteps.Add(
                    $"Add to {mainPath}: a viteFinal that appends the tailwindcss() plugin and sets esbuild.jsx = \"automatic\".");
            }
        }

        if (!main.Contains("staticDirs", StringComparison.Ordinal) && hasFonts && FrameworkAnchorPattern().IsMatch(main))
        {
            main = FrameworkAnchorPattern().Replace(main, "$1,\n  \"staticDirs\": [\"../public\"],", 1);
            changed = true;
        }

        if (changed)
        {
            File.WriteAllText(mainPath, main);
            result.Patched.Add(Path.GetRelativePath(outDir, mainPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static void PatchPreview(string storybookDir, string outDir, StorybookPresetResult result)
    {
        var previewPath = new[] { "preview.jsx", "preview.js", "preview.tsx", "preview.ts" }
            .Select(name => Path.Combine(storybookDir, name))
            .FirstOrDefault(File.Exists);

        if (previewPath is null)
        {
            return;
        }

        var modes = ReadModeNames(outDir) ?? ["dark", "light"];

        if (!previewPath.EndsWith(".jsx", StringComparison.Ordinal) && !previewPath.EndsWith(".tsx", StringComparison.Ordinal))
        {
            result.ManualSteps.Add(
                $"{previewPath} is not JSX — rename it to preview.jsx (or add the theme decorator by hand) and import \"./preview.css\".");

            return;
        }

        var preview = File.ReadAllText(previewPath);
        var changed = false;

        if (!preview.Contains("preview.css", StringComparison.Ordinal))
        {
            preview = $"import \"./preview.css\";\n{preview}";
            changed = true;
        }

        if (!preview.Contains("data-theme", StringComparison.Ordinal) && modes.Count > 1)
        {
            var (helper, previewFields) = GenerateThemeDecorator(modes);
            preview = PreviewObjectPattern().Replace(preview, $"{helper}\nconst preview = {{\n{previewFields}", 1);

            if (preview.Contains("withTheme", StringComparison.Ordinal))
            {
                changed = true;
            }
            else
            {
                result.ManualSteps.Add(
                    "Could not insert the theme decorator (preview.jsx has an unusual shape) — add a decorator "
                    + "that sets both the mode class and data-theme on the wrapper.");
            }
        }

        if (changed)
        {
            File.WriteAllText(previewPath, preview);
            result.Patched.Add(Path.GetRelativePath(outDir, previewPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private static List<string>? ReadModeNames(string outDir)
    {
        var path = Path.Combine(ExtractionManifests.Directory(outDir), ExtractionManifests.TokenModesFile);
        var raw = CodeWalker.ReadText(path);

        if (raw is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(raw, DsJson.Info<List<string>>());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (string Helper, string PreviewFields) GenerateThemeDecorator(IReadOnlyList<string> modes)
    {
        var items = new StringBuilder();

        foreach (var mode in modes)
        {
            var quoted = ScriptLiteral.Of(mode);
            items.AppendLine($"          {{ value: {quoted}, title: {quoted} }},");
        }

        var helper = string.Join('\n',
        [
            "// Theme decorator from figma_ds_setup_storybook: sets BOTH the mode class AND data-theme,",
            "// so generated tokens (.dark) and rules copied verbatim from the app",
            "// ([data-theme='...']) both apply.",
            "const withTheme = (Story, context) => {",
            $"  const theme = context.globals.theme ?? {ScriptLiteral.Of(modes[0])};",
            "  return (",
            "    <div className={theme === \"dark\" ? \"dark\" : undefined} data-theme={theme}",
            "      style={{ background: \"var(--sl-bg, var(--bg, inherit))\", color: \"var(--sl-fg, var(--fg, inherit))\", padding: 32, minHeight: \"100vh\", boxSizing: \"border-box\" }}>",
            "      <Story />",
            "    </div>",
            "  );",
            "};",
            string.Empty,
        ]);

        var previewFields = string.Join('\n',
        [
            "  globalTypes: {",
            "    theme: {",
            "      description: \"Token mode\",",
            "      toolbar: { title: \"Theme\", icon: \"circlehollow\", dynamicTitle: true,",
            "        items: [",
            items.ToString().TrimEnd('\n', '\r'),
            "        ],",
            "      },",
            "    },",
            "  },",
            $"  initialGlobals: {{ theme: {ScriptLiteral.Of(modes[0])} }},",
            "  decorators: [withTheme],",
        ]);

        return (helper, previewFields);
    }
}
