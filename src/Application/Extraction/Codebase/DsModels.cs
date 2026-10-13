using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
    public List<string> Files { get; set; } = [];
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

    public List<DsPropEntry> Props { get; set; } = [];

    public DsUsage Usage { get; set; } = new();

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

    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);

    /// <summary><c>relative/path.css:12</c> form.</summary>
    public required string Source { get; init; }
}

/// <summary>Vendor component layer detected from dependencies and files.</summary>
public sealed class DsComponentLibrary
{
    public required string Name { get; init; }

    /// <summary><c>vendored</c> — source lives in the app, <c>package</c> — comes from node_modules.</summary>
    public required string Kind { get; init; }

    public List<string> Evidence { get; set; } = [];
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

    public List<string> Frameworks { get; set; } = [];

    public List<string> StylingMethods { get; set; } = [];

    public List<DsComponentLibrary> ComponentLibraries { get; set; } = [];

    public List<DsStyleSource> StyleSources { get; set; } = [];

    public List<DsIconEntry> Icons { get; set; } = [];

    public List<DsTypographyRule> Typography { get; set; } = [];

    public DsTargetStats Stats { get; set; } = new();
}

/// <summary>Same-named components defined in several places.</summary>
public sealed class DsDuplicateGroup
{
    public required string Name { get; init; }

    public List<string> Files { get; set; } = [];
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
    public Dictionary<string, string> Levels { get; set; } = new(StringComparer.Ordinal);

    public List<DsSpecialization> Specializations { get; set; } = [];

    /// <summary>Generic components that are missing although specializations hint at them — the build list.</summary>
    public List<string> MissingPrimitives { get; set; } = [];

    public Dictionary<string, int> Summary { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/analysis.json</c>.</summary>
public sealed class DsAnalysis
{
    public int Version { get; set; } = 1;

    public string GeneratedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>Absolute directory where the design-system package will be generated.</summary>
    public required string OutDir { get; init; }

    public List<DsTargetAnalysis> Targets { get; set; } = [];

    public List<DsComponentEntry> Components { get; set; } = [];

    public DsArchitecture? Architecture { get; set; }

    public List<DsDuplicateGroup> DuplicateGroups { get; set; } = [];
}

/// <summary>Token provenance stored in <c>$extensions</c>.</summary>
public sealed class DsTokenProvenance
{
    /// <summary><c>declared</c> — from explicit configuration, <c>inferred</c> — promoted by frequency.</summary>
    public required string Confidence { get; init; }

    public List<string> Sources { get; set; } = [];

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
    public int Version { get; set; } = 1;

    public string GeneratedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    public List<string> Warnings { get; set; } = [];

    public List<DsBelowThreshold> BelowThreshold { get; set; } = [];

    public List<string> FilesWritten { get; set; } = [];
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
    public int Version { get; set; } = 1;

    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    public Dictionary<string, DsStatusEntry> Components { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Shared manifest serialization settings; manifests are read by humans and by the next run.</summary>
/// <remarks>The types are described by <see cref="DsJsonContext"/> at build time rather than read by reflection, so the server can be published with Native AOT. A property with a default is <c>set</c>, not <c>init</c>: the generated reader sets every init property at construction, and a field missing from an older manifest would arrive as null instead of its default.</remarks>
public static class DsJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    /// <summary>How <typeparamref name="T"/> is written and read with these settings; a type missing from <see cref="DsJsonContext"/> fails here.</summary>
    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = DsJsonContext.Default,
        };

        options.MakeReadOnly();

        return options;
    }
}

/// <summary>Every type the extraction writes or reads as JSON: its manifests, the provenance it leaves on tokens and the parts of an analysis a tool returns.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DsAnalysis))]
[JsonSerializable(typeof(DsStatusManifest))]
[JsonSerializable(typeof(DsTokensReport))]
[JsonSerializable(typeof(DsTokenProvenance))]
[JsonSerializable(typeof(DsComponentEntry))]
[JsonSerializable(typeof(DsUsage))]
[JsonSerializable(typeof(DsTargetStats))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<DsPropEntry>))]
[JsonSerializable(typeof(List<VerifyCheck>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(IEnumerable<DsDuplicateGroup>))]
[JsonSerializable(typeof(IEnumerable<DsSpecialization>))]
[JsonSerializable(typeof(IEnumerable<DsBelowThreshold>))]
internal sealed partial class DsJsonContext : JsonSerializerContext;
