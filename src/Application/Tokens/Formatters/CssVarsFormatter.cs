using System.Text;

namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>CSS custom properties; references stay <c>var(--…)</c> so the cascade keeps working, composite tokens are expanded.</summary>
public sealed class CssVarsFormatter : ITokenFormatter
{
    /// <summary>Configuration name of this format, <c>css-vars</c>.</summary>
    public string Format => "css-vars";

    /// <summary>Renders the document as CSS custom properties, one selector block per mode.</summary>
    public FormatResult Write(TokenDocument document, FormatOptions options)
    {
        var warnings = new List<string>();
        var resolver = new AliasResolver(document);
        var files = new List<GeneratedFile>();

        if (options is { ShouldSplitByMode: true, ShouldSplitByCollection: true })
        {
            foreach (var set in document.Sets)
            {
                foreach (var mode in set.Modes)
                {
                    files.Add(new GeneratedFile(
                        FileName(options, set.Slug, mode),
                        RenderSelector([set], mode, SelectorFor(mode), options, resolver,
                            warnings)));
                }
            }
        }
        else if (options.ShouldSplitByMode)
        {
            foreach (var mode in document.AllModes)
            {
                var sets = document.Sets.Where(set => set.Modes.Contains(mode, StringComparer.Ordinal)).ToArray();
                files.Add(new GeneratedFile(
                    FileName(options, null, mode),
                    RenderSelector(sets, mode, SelectorFor(mode), options, resolver, warnings)));
            }
        }
        else if (options.ShouldSplitByCollection)
        {
            foreach (var set in document.Sets)
            {
                files.Add(new GeneratedFile(
                    FileName(options, set.Slug, null),
                    RenderAllModes([set], options, resolver, warnings)));
            }
        }
        else
        {
            files.Add(new GeneratedFile(
                FileName(options, null, null),
                RenderAllModes(document.Sets, options, resolver, warnings)));
        }

        warnings.AddRange(resolver.Warnings);

        return new FormatResult(files, warnings);
    }

    /// <summary>Mode → selector: Light and Default in <c>:root</c>, Dark in <c>.dark</c>, others in a theme attribute.</summary>
    internal static string SelectorFor(string mode) => mode.ToLowerInvariant() switch
    {
        "default" or "light" or "value" => ":root",
        "dark" => ".dark",
        _ => $"[data-theme=\"{TokenNaming.Slugify(mode)}\"]",
    };

    private static string RenderAllModes(
        IReadOnlyCollection<TokenSet> sets,
        FormatOptions options,
        AliasResolver resolver,
        List<string> warnings)
    {
        var modes = new List<string>();

        foreach (var mode in sets.SelectMany(set => set.Modes))
        {
            if (!modes.Contains(mode, StringComparer.Ordinal))
            {
                modes.Add(mode);
            }
        }

        var builder = new StringBuilder();

        foreach (var mode in modes)
        {
            builder.Append(RenderSelector(sets, mode, SelectorFor(mode), options, resolver, warnings));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string RenderSelector(
        IReadOnlyCollection<TokenSet> sets,
        string mode,
        string selector,
        FormatOptions options,
        AliasResolver resolver,
        List<string> warnings)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{selector} {{");

        foreach (var set in sets)
        {
            foreach (var token in set.Tokens)
            {
                if (!token.Values.ContainsKey(mode) && set.Modes.Count > 1)
                {
                    continue;
                }

                AppendToken(builder, token, mode, options, resolver, warnings);
            }
        }

        builder.AppendLine("}");

        return builder.ToString();
    }

    private static void AppendToken(
        StringBuilder builder,
        Token token,
        string mode,
        FormatOptions options,
        AliasResolver resolver,
        List<string> warnings)
    {
        var name = $"--{options.Prefix}{TokenNaming.ToCssName(token.Path)}";
        var value = token.ValueFor(mode);

        if (value is null)
        {
            return;
        }

        if (value.IsReference && !options.ShouldResolveAliases)
        {
            var target = resolver.ReferenceTargetPath(value.Reference!);

            if (target is not null)
            {
                builder.AppendLine($"  {name}: var(--{options.Prefix}{TokenNaming.ToCssName(target)});");

                return;
            }

            warnings.Add($"{token.DottedPath}: the reference {{{value.Reference}}} was not found — unfolding the value.");
        }

        var literal = value.IsReference ? resolver.ResolveToLiteral(token, mode)?.Literal : value.Literal;

        if (literal is null)
        {
            warnings.Add($"{token.DottedPath}: no value, skipping.");

            return;
        }

        switch (token.Type)
        {
            case TokenTypes.Typography:
                {
                    foreach (var (field, rendered) in ValueRenderer.ExpandTypography(literal, options))
                    {
                        builder.AppendLine($"  {name}-{TokenNaming.ToKebab(field)}: {rendered};");
                    }

                    return;
                }
            case TokenTypes.Shadow:
                {
                    var shadow = ValueRenderer.RenderShadow(literal, options);

                    if (shadow is null)
                    {
                        warnings.Add($"{token.DottedPath}: the shadow did not parse, skipping.");

                        return;
                    }

                    builder.AppendLine($"  {name}: {shadow};");

                    return;
                }
            default:
                builder.AppendLine($"  {name}: {ValueRenderer.Render(literal, token.Type, options)};");
                break;
        }
    }

    internal static string FileName(FormatOptions options, string? setSlug, string? mode)
    {
        var parts = new List<string> { options.BaseFileName };

        if (setSlug is not null) parts.Add(setSlug);
        if (mode is not null) parts.Add(TokenNaming.Slugify(mode));

        return $"{string.Join('-', parts)}.css";
    }
}
