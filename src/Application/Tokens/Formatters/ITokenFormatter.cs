namespace Snail.MCP.Figma.Application.Tokens.Formatters;

/// <summary>Turns the shared token model into files of a specific format.</summary>
public interface ITokenFormatter
{
    /// <summary>Format name in configuration, e.g. <c>css-vars</c> or <c>tailwind-v4</c>.</summary>
    string Format { get; }

    /// <summary>Produces files from a token document.</summary>
    FormatResult Write(TokenDocument document, FormatOptions options);
}

/// <summary>Settings of one output target.</summary>
public sealed record FormatOptions
{
    /// <summary>Token name prefix in the output, e.g. <c>ds-</c>.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Separate file per mode.</summary>
    public bool ShouldSplitByMode { get; init; }

    /// <summary>Separate file per set.</summary>
    public bool ShouldSplitByCollection { get; init; }

    /// <summary>Resolve references to final values; CSS, SCSS and Tailwind express them natively, other formats cannot.</summary>
    public bool ShouldResolveAliases { get; init; }

    /// <summary>Output color format: hex, hex8, rgba, hsl, oklch.</summary>
    public string ColorFormat { get; init; } = "hex";

    /// <summary>Size unit: px, rem, pt, dp.</summary>
    public string? SizeUnit { get; init; }

    /// <summary>Pixels in one rem, used when sizes convert between <c>px</c> and <c>rem</c>.</summary>
    public double RemBase { get; init; } = Transforms.SizeTransform.DefaultRemBase;

    /// <summary>Output file base name without extension.</summary>
    public string BaseFileName { get; init; } = "tokens";
}

/// <summary>Produced files and everything that raised doubts.</summary>
public sealed record FormatResult(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<string> Warnings);

/// <summary>One output file.</summary>
public sealed record GeneratedFile(string Path, string Content);
