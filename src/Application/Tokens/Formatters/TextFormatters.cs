using System.Text;

namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>Shared mechanics of line-based text formats (SCSS, Less, Tailwind, TS): walking tokens while resolving references and composites.</summary>
internal static class TextFormatterCore
{
    /// <summary>Flat name → value list for one mode; references are rendered natively via <paramref name="renderReference"/> or resolved.</summary>
    public static IEnumerable<(string Name, string Value)> Flatten(
        TokenDocument document,
        string mode,
        FormatOptions options,
        AliasResolver resolver,
        List<string> warnings,
        Func<IReadOnlyList<string>, string>? renderReference = null)
    {
        foreach (var set in document.Sets)
        {
            foreach (var token in set.Tokens)
            {
                if (!token.Values.ContainsKey(mode) && set.Modes.Count > 1)
                {
                    continue;
                }

                var value = token.ValueFor(mode);

                if (value is null)
                {
                    continue;
                }

                var name = options.Prefix + TokenNaming.ToCssName(token.Path);

                if (value.IsReference && renderReference is not null && !options.ShouldResolveAliases)
                {
                    var target = resolver.ReferenceTargetPath(value.Reference!);

                    if (target is not null)
                    {
                        yield return (name, renderReference(target));
                        continue;
                    }
                }

                var literal = value.IsReference ? resolver.ResolveToLiteral(token, mode)?.Literal : value.Literal;

                if (literal is null)
                {
                    warnings.Add($"{token.DottedPath}: the value did not resolve, skipping.");
                    continue;
                }

                if (token.Type == TokenTypes.Typography)
                {
                    foreach (var (field, rendered) in ValueRenderer.ExpandTypography(literal, options))
                    {
                        yield return ($"{name}-{TokenNaming.ToKebab(field)}", rendered);
                    }

                    continue;
                }

                if (token.Type == TokenTypes.Shadow)
                {
                    var shadow = ValueRenderer.RenderShadow(literal, options);

                    if (shadow is not null)
                    {
                        yield return (name, shadow);
                    }

                    continue;
                }

                yield return (name, ValueRenderer.Render(literal, token.Type, options));
            }
        }
    }

    /// <summary>First mode of the document — the one written when files are not split by mode.</summary>
    public static string PrimaryMode(TokenDocument document) => document.AllModes.FirstOrDefault() ?? "Value";
}

/// <summary>SCSS variables such as <c>$color-primary: #FF0000;</c>; references stay $variable references.</summary>
public sealed class ScssFormatter : ITokenFormatter
{
    public string Format => "scss";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var files = new List<GeneratedFile>();

        var modes = options.ShouldSplitByMode ? document.AllModes : [TextFormatterCore.PrimaryMode(document)];

        foreach (var mode in modes)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"// Mode: {mode}");

            foreach (var (name, value) in TextFormatterCore.Flatten(
                         document, mode, options, resolver, warnings,
                         target => $"${options.Prefix}{TokenNaming.ToCssName(target)}"))
            {
                builder.AppendLine($"${name}: {value};");
            }

            var suffix = options.ShouldSplitByMode ? $"-{TokenNaming.Slugify(mode)}" : string.Empty;
            files.Add(new GeneratedFile($"{options.BaseFileName}{suffix}.scss", builder.ToString()));
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult(files, warnings);
    }
}

/// <summary>Less variables such as <c>@color-primary: #FF0000;</c>.</summary>
public sealed class LessFormatter : ITokenFormatter
{
    public string Format => "less";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var files = new List<GeneratedFile>();

        var modes = options.ShouldSplitByMode ? document.AllModes : [TextFormatterCore.PrimaryMode(document)];

        foreach (var mode in modes)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"// Mode: {mode}");

            foreach (var (name, value) in TextFormatterCore.Flatten(
                         document, mode, options, resolver, warnings,
                         target => $"@{options.Prefix}{TokenNaming.ToCssName(target)}"))
            {
                builder.AppendLine($"@{name}: {value};");
            }

            var suffix = options.ShouldSplitByMode ? $"-{TokenNaming.Slugify(mode)}" : string.Empty;
            files.Add(new GeneratedFile($"{options.BaseFileName}{suffix}.less", builder.ToString()));
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult(files, warnings);
    }
}

/// <summary>Tailwind v4: the theme is declared in CSS with <c>@theme</c>; Tailwind derives utility classes from variable names.</summary>
public sealed class TailwindV4Formatter : ITokenFormatter
{
    public string Format => "tailwind-v4";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var builder = new StringBuilder();

        builder.AppendLine("@import \"tailwindcss\";");
        builder.AppendLine();
        builder.AppendLine("@theme {");

        var primary = TextFormatterCore.PrimaryMode(document);

        foreach (var (name, value) in TextFormatterCore.Flatten(
                     document, primary, options, resolver, warnings,
                     target => $"var(--{options.Prefix}{TokenNaming.ToCssName(target)})"))
        {
            builder.AppendLine($"  --{name}: {value};");
        }

        builder.AppendLine("}");

        foreach (var mode in document.AllModes.Skip(1))
        {
            builder.AppendLine();
            builder.AppendLine($"{CssVarsFormatter.SelectorFor(mode)} {{");

            foreach (var (name, value) in TextFormatterCore.Flatten(
                         document, mode, options, resolver, warnings,
                         target => $"var(--{options.Prefix}{TokenNaming.ToCssName(target)})"))
            {
                builder.AppendLine($"  --{name}: {value};");
            }

            builder.AppendLine("}");
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult([new GeneratedFile($"{options.BaseFileName}.css", builder.ToString())], warnings);
    }
}

/// <summary>Tailwind v3: JavaScript config with a <c>theme.extend</c> section.</summary>
public sealed class TailwindV3Formatter : ITokenFormatter
{
    public string Format => "tailwind-v3";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);

        var resolveOptions = options with { ShouldResolveAliases = true };
        var primary = TextFormatterCore.PrimaryMode(document);

        var sections = new SortedDictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);

        foreach (var set in document.Sets)
        {
            foreach (var token in set.Tokens)
            {
                var literal = resolver.ResolveToLiteral(token, primary)?.Literal;

                if (literal is null)
                {
                    continue;
                }

                var section = SectionFor(token.Type);
                var key = string.Join('-', token.Path.Select(TokenNaming.Slugify));

                if (!sections.TryGetValue(section, out var entries))
                {
                    entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    sections[section] = entries;
                }

                entries[key] = token.Type == TokenTypes.Shadow
                    ? ValueRenderer.RenderShadow(literal, resolveOptions) ?? string.Empty
                    : ValueRenderer.Render(literal, token.Type, resolveOptions);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("/** @type {import('tailwindcss').Config} */");
        builder.AppendLine("module.exports = {");
        builder.AppendLine("  theme: {");
        builder.AppendLine("    extend: {");

        foreach (var (section, entries) in sections)
        {
            builder.AppendLine($"      {section}: {{");

            foreach (var (key, value) in entries)
            {
                builder.AppendLine($"        '{key}': '{value.Replace("'", "\\'")}',");
            }

            builder.AppendLine("      },");
        }

        builder.AppendLine("    },");
        builder.AppendLine("  },");
        builder.AppendLine("};");

        warnings.AddRange(resolver.Warnings);

        return new FormatResult([new GeneratedFile("tailwind.config.js", builder.ToString())], warnings);
    }

    private static string SectionFor(string type) => type switch
    {
        TokenTypes.Color => "colors",
        TokenTypes.Dimension => "spacing",
        TokenTypes.FontFamily => "fontFamily",
        TokenTypes.FontWeight => "fontWeight",
        TokenTypes.Shadow => "boxShadow",
        TokenTypes.Duration => "transitionDuration",
        _ => "extendedTokens",
    };
}

/// <summary>TypeScript module with typed constants.</summary>
public sealed class TsModuleFormatter : ITokenFormatter
{
    public string Format => "ts-module";

    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var builder = new StringBuilder();

        builder.AppendLine("// Generated from Figma tokens. Manual edits will be overwritten.");
        builder.AppendLine();
        builder.AppendLine("export const tokens = {");

        var primary = TextFormatterCore.PrimaryMode(document);
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in document.Sets)
        {
            foreach (var token in set.Tokens)
            {
                var literal = resolver.ResolveToLiteral(token, primary)?.Literal;

                if (literal is null)
                {
                    continue;
                }

                var name = TokenNaming.ToCamel(token.Path);

                if (!emitted.Add(name))
                {
                    warnings.Add($"{token.DottedPath}: the name {name} is already taken, skipping.");
                    continue;
                }

                var value = token.Type == TokenTypes.Shadow
                    ? ValueRenderer.RenderShadow(literal, options) ?? string.Empty
                    : ValueRenderer.Render(literal, token.Type, options);

                builder.AppendLine($"  {name}: '{value.Replace("'", "\\'")}',");
            }
        }

        builder.AppendLine("} as const;");
        builder.AppendLine();
        builder.AppendLine("export type TokenName = keyof typeof tokens;");

        warnings.AddRange(resolver.Warnings);

        return new FormatResult([new GeneratedFile($"{options.BaseFileName}.ts", builder.ToString())], warnings);
    }
}
