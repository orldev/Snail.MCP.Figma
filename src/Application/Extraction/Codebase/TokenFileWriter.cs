using Snail.MCP.Figma.Application.Tokens;
using Snail.MCP.Figma.Application.Tokens.Formatters;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>What was written to disk and what raised doubts.</summary>
public sealed record TokenWriteResult(List<string> FilesWritten, List<string> Warnings);

/// <summary>Lays the token document out into package files with canonical names used by verification, Storybook and re-import.</summary>
public sealed class TokenFileWriter(IWorkspace workspace)
{
    /// <summary>Formats the package can write, and where.</summary>
    public static readonly IReadOnlyDictionary<string, string> CanonicalFileNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dtcg"] = "tokens/tokens.json",
            ["css-vars"] = "tokens/tokens.css",
            ["tailwind-v4"] = "tokens/theme.css",
            ["tailwind-v3"] = "tokens/tailwind.tokens.js",
            ["scss"] = "tokens/_tokens.scss",
            ["less"] = "tokens/_tokens.less",
            ["ts-module"] = "tokens/tokens.ts",
            ["json-nested"] = "tokens/tokens.nested.json",
            ["json-flat"] = "tokens/tokens.flat.json",
            ["tokens-studio"] = "tokens/tokens-studio.json",
            ["style-dictionary-v3"] = "tokens/style-dictionary.json",
        };

    /// <summary>Canonical DTCG is always written: verification and Figma import read it.</summary>
    public static IReadOnlyList<string> WithCanonicalFirst(IEnumerable<string>? formats)
    {
        var result = new List<string> { "dtcg" };

        foreach (var format in formats ?? [])
        {
            if (!result.Contains(format, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(format);
            }
        }

        return result;
    }

    /// <summary>Writes the document in every requested format; token files are always refreshed.</summary>
    public TokenWriteResult Write(TokenDocument document, string outDir, IEnumerable<string> formats)
    {
        var written = new List<string>();
        var warnings = new List<string>();

        foreach (var format in formats)
        {
            var formatter = TokenFormatters.Find(format);

            if (formatter is null)
            {
                warnings.Add($"Format {format} is unknown — skipped. Available: {string.Join(", ", TokenFormatters.Formats)}.");
                continue;
            }

            try
            {
                var result = formatter.Write(document, new FormatOptions
                {
                    ShouldResolveAliases = !TokenFormatters.SupportsAliases(formatter.Format),
                });

                warnings.AddRange(result.Warnings);

                foreach (var file in result.Files)
                {
                    var relative = result.Files.Count == 1 && CanonicalFileNames.TryGetValue(formatter.Format, out var canonical)
                        ? canonical
                        : $"tokens/{file.Path}";

                    var absolute = Path.Combine(outDir, relative.Replace('/', Path.DirectorySeparatorChar));
                    workspace.WriteText(absolute, file.Content);
                    written.Add(absolute);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                warnings.Add($"Format {format} failed to write: {ex.Message}");
            }
        }

        return new TokenWriteResult(written, warnings);
    }

    /// <summary>Mode names for the Storybook theme toolbar: lowercase, base mode first.</summary>
    public static List<string> ModeNames(TokenDocument document) =>
        [.. (document.Sets.FirstOrDefault()?.Modes ?? ["Default"])
            .Select(mode => mode.ToLowerInvariant())
            .Where(mode => mode != "default")];

    /// <summary>First token value as a string — how the showcase sees it.</summary>
    public static string FirstLiteral(Token token)
    {
        var value = token.Values.Values.FirstOrDefault();

        if (value is null)
        {
            return string.Empty;
        }

        if (value.IsReference)
        {
            return $"{{{value.Reference}}}";
        }

        return value.Literal switch
        {
            null => string.Empty,
            JsonValue json when json.TryGetValue<string>(out var text) => text,
            var other => other.ToJsonString(),
        };
    }
}
