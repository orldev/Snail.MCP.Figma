using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Framework, styling and classification names are strings, not enums: manifests outlive server updates and unknown values must pass through.</summary>
public static class DsFrameworks
{
    /// <summary>A React application without a meta-framework.</summary>
    public const string React = "react";
    /// <summary>A Next.js application, React with file-based routing.</summary>
    public const string Next = "next";
    /// <summary>An Astro site, whose components live in <c>.astro</c> files.</summary>
    public const string Astro = "astro";
    /// <summary>An Angular application.</summary>
    public const string Angular = "angular";
    /// <summary>Custom elements, typically written with Lit.</summary>
    public const string WebComponents = "web-components";
    /// <summary>A Vue application, whose components live in <c>.vue</c> files.</summary>
    public const string Vue = "vue";
    /// <summary>A Svelte application, whose components live in <c>.svelte</c> files.</summary>
    public const string Svelte = "svelte";
    /// <summary>The value for an application whose framework none of the detectors recognised.</summary>
    public const string Unknown = "unknown";

    /// <summary>Every recognised framework, leaving out <see cref="Unknown"/>.</summary>
    public static readonly IReadOnlyList<string> All =
        [React, Next, Astro, Angular, WebComponents, Vue, Svelte];
}

/// <summary>Styling methods we recognize and can back with token files.</summary>
public static class DsStylingMethods
{
    /// <summary>Tailwind CSS 4, configured in CSS through <c>@theme</c>.</summary>
    public const string TailwindV4 = "tailwind-v4";
    /// <summary>Tailwind CSS 3, configured through a <c>tailwind.config</c> file.</summary>
    public const string TailwindV3 = "tailwind-v3";
    /// <summary>CSS Modules, styles scoped per component file.</summary>
    public const string CssModules = "css-modules";
    /// <summary>Sass in its SCSS syntax.</summary>
    public const string Scss = "scss";
    /// <summary>Plain CSS files without a preprocessor or a utility framework.</summary>
    public const string VanillaCss = "vanilla-css";
    /// <summary>CSS-in-JS through Emotion.</summary>
    public const string Emotion = "emotion";
    /// <summary>CSS-in-JS through styled-components.</summary>
    public const string StyledComponents = "styled-components";
}

/// <summary>Relation of a component to vendor code: <c>vendored</c>, <c>wrapped</c>, <c>pure-vendor</c> or <c>bespoke</c> — it defines what porting means.</summary>
public static class DsClassifications
{
    /// <summary>The vendor's source was copied into the application and is owned by it now, as shadcn/ui does.</summary>
    public const string Vendored = "vendored";
    /// <summary>An application component that wraps a vendor component imported from a package.</summary>
    public const string Wrapped = "wrapped";
    /// <summary>A vendor component used directly from its package, with no source in the application.</summary>
    public const string PureVendor = "pure-vendor";
    /// <summary>A component written in the application with no vendor behind it.</summary>
    public const string Bespoke = "bespoke";
}

/// <summary>Porting statuses of a component.</summary>
public static class DsPortStatuses
{
    /// <summary>The component has not been ported yet.</summary>
    public const string Pending = "pending";
    /// <summary>Porting of the component has started but is not finished.</summary>
    public const string InProgress = "in-progress";
    /// <summary>The component exists in the design-system package.</summary>
    public const string Ported = "ported";
    /// <summary>The component was deliberately left out of the port.</summary>
    public const string Skipped = "skipped";

    /// <summary>Every porting status a component may be set to.</summary>
    public static readonly IReadOnlyList<string> All = [Pending, InProgress, Ported, Skipped];
}

/// <summary>One component prop pulled from source.</summary>
public sealed class DsPropEntry
{
    /// <summary>The prop's name as declared in the component's source.</summary>
    public required string Name { get; init; }

    /// <summary>Type as written in source, best-effort by regex.</summary>
    public string? Type { get; set; }

    /// <summary>Whether a caller must pass the prop, as far as the declaration shows.</summary>
    [SuppressMessage("Snail", "S3L0008", Justification = "required is a field of the manifests written into the analysed project and of the answers of figma_ds_* tools; the next run reads them back.")]
    public bool Required { get; set; }

    /// <summary>The default the declaration gives the prop, as written in source; <c>null</c> when none was found.</summary>
    public string? DefaultValue { get; set; }
}

/// <summary>Real usage count — the porting order is ranked by it.</summary>
public sealed class DsUsage
{
    /// <summary>Number of other files that import the component.</summary>
    public int ImportCount { get; set; }

    /// <summary>Number of times the component's tag appears in other files.</summary>
    public int TagCount { get; set; }

    /// <summary>Sample of using files, as relative paths.</summary>
    public List<string> Files { get; set; } = [];
}

/// <summary>Inventory component.</summary>
public sealed class DsComponentEntry
{
    /// <summary>The component's name as the inventory found it in source.</summary>
    public required string Name { get; init; }

    /// <summary>Path relative to the target root; absent for pure-vendor entries.</summary>
    public string? File { get; init; }

    /// <summary>Target index in <see cref="DsAnalysis.Targets"/>.</summary>
    public int TargetIndex { get; init; }

    /// <summary>The framework the component is written for, one of <see cref="DsFrameworks"/>.</summary>
    public required string Framework { get; init; }

    /// <summary>The component's relation to vendor code, one of <see cref="DsClassifications"/>.</summary>
    public required string Classification { get; init; }

    /// <summary>The vendor package the component comes from or wraps; <c>null</c> for a bespoke one.</summary>
    public string? VendorModule { get; init; }

    /// <summary>How the component is exported from its file, <c>named</c> or <c>default</c>; <c>null</c> when it is not defined in the application.</summary>
    public string? ExportKind { get; init; }

    /// <summary>The props the component's declaration accepts.</summary>
    public List<DsPropEntry> Props { get; set; } = [];

    /// <summary>How often the rest of the application imports and renders the component.</summary>
    public DsUsage Usage { get; set; } = new();

    /// <summary>Literal prop values seen at call sites — raw material for inferring variants.</summary>
    public Dictionary<string, List<string>>? ObservedProps { get; set; }

    /// <summary>Same-named components elsewhere — the classic sign of a component written three times.</summary>
    public List<string>? Duplicates { get; set; }
}

/// <summary>File that affects styling.</summary>
public sealed class DsStyleSource
{
    /// <summary>Path of the file relative to the target root.</summary>
    public required string File { get; init; }

    /// <summary>What sort of styling file it is, such as <c>tailwind-config</c>, <c>css-module</c>, <c>scss</c> or <c>css</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>A remark on what the file holds, such as how many custom properties it declares; <c>null</c> when there is nothing to say.</summary>
    public string? Note { get; init; }
}

/// <summary>Icon found in code.</summary>
public sealed class DsIconEntry
{
    /// <summary>The icon's name, taken from its file or its component.</summary>
    public required string Name { get; init; }

    /// <summary>Path of the file the icon was found in, relative to the target root.</summary>
    public required string File { get; init; }

    /// <summary><c>svg-file</c> or <c>inline-svg</c>.</summary>
    public required string Source { get; init; }
}

/// <summary>Typography rule from styles — the basis of the typography showcase.</summary>
public sealed class DsTypographyRule
{
    /// <summary>The CSS selector the rule applies to.</summary>
    public required string Selector { get; init; }

    /// <summary>The rule's typography declarations, keyed by CSS property name.</summary>
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);

    /// <summary><c>relative/path.css:12</c> form.</summary>
    public required string Source { get; init; }
}

/// <summary>Vendor component layer detected from dependencies and files.</summary>
public sealed class DsComponentLibrary
{
    /// <summary>The library's display name, such as <c>shadcn/ui</c> or <c>Radix UI</c>.</summary>
    public required string Name { get; init; }

    /// <summary><c>vendored</c> — source lives in the app, <c>package</c> — comes from node_modules.</summary>
    public required string Kind { get; init; }

    /// <summary>The dependencies, configuration files or directories that revealed the library.</summary>
    public List<string> Evidence { get; set; } = [];
}

/// <summary>How much of one application the scan covered and how long it took.</summary>
public sealed class DsTargetStats
{
    /// <summary>Number of files that reached analysis.</summary>
    public int FilesScanned { get; set; }

    /// <summary>Number of files the walk left out, by filter or by size.</summary>
    public int FilesSkipped { get; set; }

    /// <summary>How long the scan of the application took, in milliseconds.</summary>
    public long DurationMs { get; set; }
}

/// <summary>Analysis of one application.</summary>
public sealed class DsTargetAnalysis
{
    /// <summary>The application's root directory.</summary>
    public required string Root { get; init; }

    /// <summary>The name from the application's <c>package.json</c>; <c>null</c> when it has none.</summary>
    public string? PackageName { get; init; }

    /// <summary>The frameworks detected in the application, values of <see cref="DsFrameworks"/>.</summary>
    public List<string> Frameworks { get; set; } = [];

    /// <summary>The styling methods detected in the application, values of <see cref="DsStylingMethods"/>.</summary>
    public List<string> StylingMethods { get; set; } = [];

    /// <summary>The vendor component libraries the application relies on.</summary>
    public List<DsComponentLibrary> ComponentLibraries { get; set; } = [];

    /// <summary>The files that affect the application's styling.</summary>
    public List<DsStyleSource> StyleSources { get; set; } = [];

    /// <summary>The icons found in the application's code and assets.</summary>
    public List<DsIconEntry> Icons { get; set; } = [];

    /// <summary>The typography rules found in the application's styles.</summary>
    public List<DsTypographyRule> Typography { get; set; } = [];

    /// <summary>How much of the application the scan covered and how long it took.</summary>
    public DsTargetStats Stats { get; set; } = new();
}

/// <summary>Same-named components defined in several places.</summary>
public sealed class DsDuplicateGroup
{
    /// <summary>The name the components share.</summary>
    public required string Name { get; init; }

    /// <summary>Every file that defines a component of that name.</summary>
    public List<string> Files { get; set; } = [];
}

/// <summary>Domain name instead of a generic one — what separates a widget pile from a design system.</summary>
public sealed class DsSpecialization
{
    /// <summary>The name of the domain-specific component, such as <c>FollowButton</c>.</summary>
    public required string Component { get; init; }

    /// <summary>The generic component it specialises, such as <c>Button</c>.</summary>
    public required string GenericOf { get; init; }

    /// <summary>The words of the name left after the generic noun, which make it domain-specific.</summary>
    public required string Qualifier { get; init; }

    /// <summary>Advice to turn the component into a variant or recipe of the generic one.</summary>
    public required string Suggestion { get; init; }
}

/// <summary>Inventory split by atomic-design level plus the generalization plan.</summary>
public sealed class DsArchitecture
{
    /// <summary>The atomic-design level of each component, keyed by component name.</summary>
    public Dictionary<string, string> Levels { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Components whose names specialise a generic one and should become its variants.</summary>
    public List<DsSpecialization> Specializations { get; set; } = [];

    /// <summary>Generic components that are missing although specializations hint at them — the build list.</summary>
    public List<string> MissingPrimitives { get; set; } = [];

    /// <summary>How many components fall on each atomic-design level.</summary>
    public Dictionary<string, int> Summary { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/analysis.json</c>.</summary>
public sealed class DsAnalysis
{
    /// <summary>Schema version of the manifest.</summary>
    public int Version { get; set; } = 1;

    /// <summary>When the analysis was written, as an ISO 8601 timestamp in UTC.</summary>
    public string GeneratedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>Absolute directory where the design-system package will be generated.</summary>
    public required string OutDir { get; init; }

    /// <summary>One analysis per scanned application.</summary>
    public List<DsTargetAnalysis> Targets { get; set; } = [];

    /// <summary>The component inventory across every target.</summary>
    public List<DsComponentEntry> Components { get; set; } = [];

    /// <summary>The inventory split by atomic-design level; <c>null</c> until classified.</summary>
    public DsArchitecture? Architecture { get; set; }

    /// <summary>Component names defined in more than one place.</summary>
    public List<DsDuplicateGroup> DuplicateGroups { get; set; } = [];
}

/// <summary>Token provenance stored in <c>$extensions</c>.</summary>
public sealed class DsTokenProvenance
{
    /// <summary><c>declared</c> — from explicit configuration, <c>inferred</c> — promoted by frequency.</summary>
    public required string Confidence { get; init; }

    /// <summary><c>path:line</c> locations the token's value was found at.</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>How many times an inferred token's value occurred; <c>null</c> for a declared one.</summary>
    public int? Frequency { get; init; }

    /// <summary>Where the value came from or what to do with it, such as renaming a token promoted by frequency.</summary>
    public string? Note { get; init; }

    /// <summary>Namespace key in <c>$extensions</c>, compatible with the donor.</summary>
    public const string ExtensionKey = "figma-console-mcp.extraction";
}

/// <summary>Value that occurred often but did not cross the threshold.</summary>
public sealed class DsBelowThreshold
{
    /// <summary>The raw value that was counted.</summary>
    public required string Value { get; init; }

    /// <summary>The token type the value would have become, such as color or dimension.</summary>
    public required string Type { get; init; }

    /// <summary>How many times the value occurred.</summary>
    public int Frequency { get; init; }
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/tokens-report.json</c>.</summary>
public sealed class DsTokensReport
{
    /// <summary>Schema version of the manifest.</summary>
    public int Version { get; set; } = 1;

    /// <summary>When the report was written, as an ISO 8601 timestamp in UTC.</summary>
    public string GeneratedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>Number of tokens extracted, keyed by token type.</summary>
    public Dictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Problems met while extracting or writing tokens that did not stop it.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Values that occurred often but too rarely to become tokens.</summary>
    public List<DsBelowThreshold> BelowThreshold { get; set; } = [];

    /// <summary>Paths of the token files written.</summary>
    public List<string> FilesWritten { get; set; } = [];
}

/// <summary>The porting state of one component.</summary>
public sealed class DsStatusEntry
{
    /// <summary>Where the component stands, one of <see cref="DsPortStatuses"/>.</summary>
    public required string Status { get; set; }

    /// <summary>Free-form notes left with the last status change.</summary>
    public string? Notes { get; set; }

    /// <summary>Path of the story written for the component, once there is one.</summary>
    public string? StoryFile { get; set; }

    /// <summary>When the status last changed, as an ISO 8601 timestamp in UTC.</summary>
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

/// <summary>Manifest <c>&lt;outDir&gt;/.extraction/status.json</c>: porting progress that survives sessions.</summary>
public sealed class DsStatusManifest
{
    /// <summary>Schema version of the manifest.</summary>
    public int Version { get; set; } = 1;

    /// <summary>When any component's status last changed, as an ISO 8601 timestamp in UTC.</summary>
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");

    /// <summary>The porting state of each component, keyed by component name.</summary>
    public Dictionary<string, DsStatusEntry> Components { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Shared manifest serialization settings; manifests are read by humans and by the next run.</summary>
/// <remarks>The types are described by <see cref="DsJsonContext"/> at build time rather than read by reflection, so the server can be published with Native AOT. A property with a default is <c>set</c>, not <c>init</c>: the generated reader sets every init property at construction, and a field missing from an older manifest would arrive as null instead of its default.</remarks>
public static class DsJson
{
    /// <summary>The read-only serializer settings every manifest is written and read with: indented camel-case JSON that leaves out nulls.</summary>
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
