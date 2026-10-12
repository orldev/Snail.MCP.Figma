using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Framework, styling and classification names are strings, not enums: manifests outlive server updates and unknown values must pass through.</summary>
public static class DsFrameworks
{
    public const string React = "react";
    public const string Next = "next";
    public const string Astro = "astro";
    public const string Angular = "angular";
    public const string WebComponents = "web-components";
    public const string Vue = "vue";
    public const string Svelte = "svelte";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All =
        [React, Next, Astro, Angular, WebComponents, Vue, Svelte];
}

/// <summary>Styling methods we recognize and can back with token files.</summary>
public static class DsStylingMethods
{
    public const string TailwindV4 = "tailwind-v4";
    public const string TailwindV3 = "tailwind-v3";
    public const string CssModules = "css-modules";
    public const string Scss = "scss";
    public const string VanillaCss = "vanilla-css";
    public const string Emotion = "emotion";
    public const string StyledComponents = "styled-components";
}

/// <summary>Relation of a component to vendor code: <c>vendored</c>, <c>wrapped</c>, <c>pure-vendor</c> or <c>bespoke</c> — it defines what porting means.</summary>
public static class DsClassifications
{
    public const string Vendored = "vendored";
    public const string Wrapped = "wrapped";
    public const string PureVendor = "pure-vendor";
    public const string Bespoke = "bespoke";
}

/// <summary>Porting statuses of a component.</summary>
public static class DsPortStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in-progress";
    public const string Ported = "ported";
    public const string Skipped = "skipped";

    public static readonly IReadOnlyList<string> All = [Pending, InProgress, Ported, Skipped];
}

/// <summary>One component prop pulled from source.</summary>
public sealed class DsPropEntry
{
    public required string Name { get; init; }

    /// <summary>Type as written in source, best-effort by regex.</summary>
    public string? Type { get; set; }

    [SuppressMessage("Snail", "S3L0008", Justification = "required is a field of the manifests written into the analysed project and of the answers of figma_ds_* tools; the next run reads them back.")]
    public bool Required { get; set; }

    public string? DefaultValue { get; set; }
}

/// <summary>Real usage count — the porting order is ranked by it.</summary>
public sealed class DsUsage
{
    public int ImportCount { get; set; }

    public int TagCount { get; set; }

    /// <summary>Sample of using files, as relative paths.</summary>
    public List<string> Files { get; init; } = [];
}

/// <summary>Inventory component.</summary>
public sealed class DsComponentEntry
{
    public required string Name { get; init; }

    /// <summary>Path relative to the target root; absent for pure-vendor entries.</summary>
    public string? File { get; init; }

    /// <summary>Target index in <see cref="DsAnalysis.Targets"/>.</summary>
    public int TargetIndex { get; init; }

    public required string Framework { get; init; }

    public required string Classification { get; init; }

    public string? VendorModule { get; init; }

    public string? ExportKind { get; init; }

    public List<DsPropEntry> Props { get; init; } = [];

    public DsUsage Usage { get; init; } = new();

    /// <summary>Literal prop values seen at call sites — raw material for inferring variants.</summary>
    public Dictionary<string, List<string>>? ObservedProps { get; set; }

    /// <summary>Same-named components elsewhere — the classic sign of a component written three times.</summary>
    public List<string>? Duplicates { get; set; }
}

/// <summary>File that affects styling.</summary>
public sealed class DsStyleSource
{
    public required string File { get; init; }

    public required string Kind { get; init; }

    public string? Note { get; init; }
}

/// <summary>Icon found in code.</summary>
public sealed class DsIconEntry
{
    public required string Name { get; init; }

    public required string File { get; init; }

    /// <summary><c>svg-file</c> or <c>inline-svg</c>.</summary>
    public required string Source { get; init; }
}

/// <summary>Typography rule from styles — the basis of the typography showcase.</summary>
public sealed class DsTypographyRule
{
    public required string Selector { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.Ordinal);

    /// <summary><c>relative/path.css:12</c> form.</summary>
    public required string Source { get; init; }
}

/// <summary>Vendor component layer detected from dependencies and files.</summary>
public sealed class DsComponentLibrary
{
    public required string Name { get; init; }

    /// <summary><c>vendored</c> — source lives in the app, <c>package</c> — comes from node_modules.</summary>
    public required string Kind { get; init; }

    public List<string> Evidence { get; init; } = [];
}

public sealed class DsTargetStats
{
    public int FilesScanned { get; set; }

    public int FilesSkipped { get; set; }

    public long DurationMs { get; set; }
}

/// <summary>Analysis of one application.</summary>
public sealed class DsTargetAnalysis
{
    public required string Root { get; init; }

    public string? PackageName { get; init; }

    public List<string> Frameworks { get; init; } = [];

    public List<string> StylingMethods { get; init; } = [];

    public List<DsComponentLibrary> ComponentLibraries { get; init; } = [];

    public List<DsStyleSource> StyleSources { get; init; } = [];

    public List<DsIconEntry> Icons { get; init; } = [];

    public List<DsTypographyRule> Typography { get; init; } = [];

    public DsTargetStats Stats { get; init; } = new();
}

/// <summary>Same-named components defined in several places.</summary>
public sealed class DsDuplicateGroup
{
    public required string Name { get; init; }

    public List<string> Files { get; init; } = [];
}

/// <summary>Domain name instead of a generic one — what separates a widget pile from a design system.</summary>
public sealed class DsSpecialization
{
    public required string Component { get; init; }

    public required string GenericOf { get; init; }

    public required string Qualifier { get; init; }

    public required string Suggestion { get; init; }
}

/// <summary>Inventory split by atomic-design level plus the generalization plan.</summary>
public sealed class DsArchitecture
{
    public Dictionary<string, string> Levels { get; init; } = new(StringComparer.Ordinal);

    public List<DsSpecialization> Specializations { get; init; } = [];

    /// <summary>Generic components that are missing although specializations hint at them — the build list.</summary>
    public List<string> MissingPrimitives { get; init; } = [];

    public Dictionary<string, int> Summary { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/analysis.json</c>.</summary>
public sealed class DsAnalysis
{
    public int Version { get; init; } = 1;

    public string GeneratedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>Absolute directory where the design-system package will be generated.</summary>
    public required string OutDir { get; init; }

    public List<DsTargetAnalysis> Targets { get; init; } = [];

    public List<DsComponentEntry> Components { get; init; } = [];

    public DsArchitecture? Architecture { get; set; }

    public List<DsDuplicateGroup> DuplicateGroups { get; init; } = [];
}

/// <summary>Token provenance stored in <c>$extensions</c>.</summary>
public sealed class DsTokenProvenance
{
    /// <summary><c>declared</c> — from explicit configuration, <c>inferred</c> — promoted by frequency.</summary>
    public required string Confidence { get; init; }

    public List<string> Sources { get; init; } = [];

    public int? Frequency { get; init; }

    public string? Note { get; init; }

    /// <summary>Namespace key in <c>$extensions</c>, compatible with the donor.</summary>
    public const string ExtensionKey = "figma-console-mcp.extraction";
}

/// <summary>Value that occurred often but did not cross the threshold.</summary>
public sealed class DsBelowThreshold
{
    public required string Value { get; init; }

    public required string Type { get; init; }

    public int Frequency { get; init; }
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/tokens-report.json</c>.</summary>
public sealed class DsTokensReport
{
    public int Version { get; init; } = 1;

    public string GeneratedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");

    public Dictionary<string, int> Counts { get; init; } = new(StringComparer.Ordinal);

    public List<string> Warnings { get; init; } = [];

    public List<DsBelowThreshold> BelowThreshold { get; init; } = [];

    public List<string> FilesWritten { get; init; } = [];
}

public sealed class DsStatusEntry
{
    public required string Status { get; set; }

    public string? Notes { get; set; }

    public string? StoryFile { get; set; }

    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/status.json</c>: porting progress that survives sessions.</summary>
public sealed class DsStatusManifest
{
    public int Version { get; init; } = 1;

    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public Dictionary<string, DsStatusEntry> Components { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Shared manifest serialization settings; manifests are read by humans and by the next run.</summary>
public static class DsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
