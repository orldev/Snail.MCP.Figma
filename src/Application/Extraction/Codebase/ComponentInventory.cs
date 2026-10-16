using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Inventory and duplicate groups of one target.</summary>
public sealed record InventoryResult(List<DsComponentEntry> Components, List<DsDuplicateGroup> DuplicateGroups);

/// <summary>Component inventory by regex: finds what the app really uses, its vendor relation, prop contracts and usage counts, reading each file once.</summary>
public static partial class ComponentInventory
{
    /// <summary>Import prefixes that mean a wrapper or pure vendor usage.</summary>
    private static readonly string[] VendorPrefixes =
    [
        "@mui/", "@material/", "@angular/material", "@chakra-ui/", "antd", "@mantine/",
        "@radix-ui/", "@headlessui/", "@ariakit/", "react-aria-components", "react-bootstrap",
        "@shoelace-style/", "@spectrum-web-components/",
    ];

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tsx", ".jsx", ".ts", ".js", ".mjs", ".html", ".astro",
    };

    private const int MaxAttributeSamplesPerTag = 40;
    private const int MaxObservedValues = 20;
    private const int MaxUsageFiles = 12;
    private const int MaxProps = 60;

    [GeneratedRegex(@"^\s*(?:readonly\s+)?([A-Za-z_$][\w$]*)(\?)?\s*:\s*([^;\n]+)[;,]?\s*$", RegexOptions.Multiline)]
    private static partial Regex PropsMemberPattern();

    [GeneratedRegex(@"([A-Za-z_$][\w$]*)\s*=\s*(""[^""]*""|'[^']*'|[\w.\[\]]+)")]
    private static partial Regex DefaultValuePattern();

    [GeneratedRegex(@"<[A-Z][\w.]*[\s/>]")]
    private static partial Regex JsxTagProbe();

    [GeneratedRegex(@"(^|/)(app|pages)/")]
    private static partial Regex RouteDirectoryPattern();

    [GeneratedRegex(@"(^|/)(page|layout|error|loading|not-found|template|default|route|_app|_document|index)\.(tsx|jsx|ts|js)$")]
    private static partial Regex RouteEntryFilePattern();

    [GeneratedRegex(@"export\s+default\s+(?:async\s+)?function\s+([A-Z][\w$]*)")]
    private static partial Regex ExportDefaultFunctionPattern();

    [GeneratedRegex(@"export\s+(?:async\s+)?function\s+([A-Z][\w$]*)\s*[(<]")]
    private static partial Regex ExportNamedFunctionPattern();

    [GeneratedRegex(@"export\s+const\s+([A-Z][\w$]*)(?:\s*:\s*[^=]+)?\s*=\s*(?:React\.)?(?:forwardRef|memo)?\s*[(<]")]
    private static partial Regex ExportConstComponentPattern();

    [GeneratedRegex(@"export\s+default\s+([A-Z][\w$]*)\s*;?")]
    private static partial Regex ExportDefaultIdentifierPattern();

    [GeneratedRegex(@"export\s*\{([^}]*)\}")]
    private static partial Regex ExportListPattern();

    [GeneratedRegex(@"@Component\s*\(")]
    private static partial Regex AngularComponentPattern();

    [GeneratedRegex(@"selector\s*:\s*['""]([^'""]+)['""]")]
    private static partial Regex AngularSelectorPattern();

    [GeneratedRegex(@"export\s+class\s+([\w$]+)")]
    private static partial Regex ExportClassPattern();

    [GeneratedRegex(@"@Input\(\)\s+(?:readonly\s+)?([\w$]+)(\!|\?)?\s*(?::\s*([^;=\n]+))?")]
    private static partial Regex AngularInputPattern();

    [GeneratedRegex(@"customElements\.define\(\s*['""]([a-z][\w-]*)['""]\s*,\s*([\w$]+)")]
    private static partial Regex CustomElementsDefinePattern();

    [GeneratedRegex(@"@customElement\(\s*['""]([a-z][\w-]*)['""]\s*\)\s*(?:export\s+)?class\s+([\w$]+)")]
    private static partial Regex CustomElementDecoratorPattern();

    [GeneratedRegex(@"@property\([^)]*\)\s+(?:accessor\s+)?([\w$]+)(\?)?\s*(?::\s*([^;=\n]+))?")]
    private static partial Regex LitPropertyPattern();

    [GeneratedRegex(@"(^|/)pages/")]
    private static partial Regex AstroPagesPattern();

    [GeneratedRegex(@"(?:interface|type)\s+Props\b[^{=]*[{=]")]
    private static partial Regex AstroPropsPattern();

    [GeneratedRegex(@"const\s*\{([^}]*)\}\s*=\s*Astro\.props")]
    private static partial Regex AstroDestructurePattern();

    [GeneratedRegex(@"from\s+['""]([^'""]+)['""]")]
    private static partial Regex FromModulePattern();

    [GeneratedRegex(@"import\s+(?:([\w$]+)\s*,?\s*)?(?:\{([^}]*)\})?\s*from\s+['""]([^'""]+)['""]")]
    private static partial Regex ImportStatementPattern();

    [GeneratedRegex(@"<([A-Za-z][\w.-]*)([^<>]{0,400}?)/?>")]
    private static partial Regex OpenTagPattern();

    [GeneratedRegex(@"([A-Za-z_$][\w-]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|\{\s*(true|false|-?\d+(?:\.\d+)?|""[^""]*""|'[^']*')\s*\})")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"(^|/)components/ui/")]
    private static partial Regex ShadcnPathPattern();

    [GeneratedRegex(@"^[A-Z][\w$]*$")]
    private static partial Regex ComponentNamePattern();

    [GeneratedRegex(@"[^\w$]")]
    private static partial Regex NonIdentifierCharacterPattern();

    /// <summary>Component definition found in one file.</summary>
    private sealed class Definition
    {
        public required string Name { get; init; }

        public required string RelativePath { get; init; }

        public required string Framework { get; init; }

        public required string ExportKind { get; init; }

        public List<DsPropEntry> Props { get; init; } = [];

        public List<string> VendorImports { get; init; } = [];

        /// <summary>Angular selector or custom-element tag, if any.</summary>
        public string? ElementTag { get; init; }
    }

    /// <summary>Small per-file index: everything needed to count usage.</summary>
    private sealed class FileIndex
    {
        public required string RelativePath { get; init; }

        public Dictionary<string, int> JsxTags { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ElementTags { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Imports { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<string>> AttributeSamples { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The components defined in the walked source files with their usage counts, vendor relation and duplicate groups, each tagged with the given target index.</summary>
    public static InventoryResult Build(IReadOnlyList<WalkedFile> files, int targetIndex)
    {
        var definitions = new List<Definition>();
        var indexes = new List<FileIndex>();

        foreach (var file in files)
        {
            if (!SourceExtensions.Contains(file.Extension))
            {
                continue;
            }

            var text = CodeWalker.ReadText(file.Path);

            if (text is null)
            {
                continue;
            }

            if (file.Extension == ".astro")
            {
                definitions.AddRange(ExtractAstroDefinition(text, file.RelativePath));
            }
            else if (file.Extension != ".html")
            {
                definitions.AddRange(ExtractReactDefinitions(text, file.RelativePath, file.Extension));
                definitions.AddRange(ExtractAngularDefinitions(text, file.RelativePath));
                definitions.AddRange(ExtractWebComponentDefinitions(text, file.RelativePath));
            }

            indexes.Add(IndexFile(text, file.RelativePath));
        }

        var components = new List<DsComponentEntry>();
        var byName = new Dictionary<string, List<Definition>>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (!byName.TryGetValue(definition.Name, out var group))
            {
                group = [];
                byName[definition.Name] = group;
            }

            group.Add(definition);
        }

        foreach (var definition in definitions)
        {
            var lookupTag = definition.ElementTag ?? definition.Name;
            var usingFiles = new List<string>();
            var tagCount = 0;
            var importCount = 0;
            var samples = new List<string>();

            foreach (var index in indexes)
            {
                if (string.Equals(index.RelativePath, definition.RelativePath, StringComparison.Ordinal))
                {
                    continue;
                }

                var count = definition.ElementTag is { } tag
                    ? index.ElementTags.GetValueOrDefault(tag)
                    : index.JsxTags.GetValueOrDefault(definition.Name);
                var imported = index.Imports.ContainsKey(definition.Name);

                if (imported)
                {
                    importCount++;
                }

                if (count > 0 || imported)
                {
                    tagCount += count;

                    if (usingFiles.Count < MaxUsageFiles)
                    {
                        usingFiles.Add(index.RelativePath);
                    }

                    if (index.AttributeSamples.TryGetValue(lookupTag, out var attributeSamples))
                    {
                        samples.AddRange(attributeSamples);
                    }
                }
            }

            var (classification, vendorModule) = Classify(definition);
            var duplicates = byName[definition.Name]
                .Where(d => !string.Equals(d.RelativePath, definition.RelativePath, StringComparison.Ordinal))
                .Select(d => d.RelativePath)
                .ToList();

            components.Add(new DsComponentEntry
            {
                Name = definition.Name,
                File = definition.RelativePath,
                TargetIndex = targetIndex,
                Framework = definition.Framework,
                Classification = classification,
                VendorModule = vendorModule,
                ExportKind = definition.ExportKind,
                Props = definition.Props,
                Usage = new DsUsage { ImportCount = importCount, TagCount = tagCount, Files = usingFiles },
                ObservedProps = samples.Count > 0 ? ObservedPropsFromSamples(samples) : null,
                Duplicates = duplicates.Count > 0 ? duplicates : null,
            });
        }

        var definedNames = new HashSet<string>(definitions.Select(d => d.Name), StringComparer.Ordinal);
        var vendorUsage = new Dictionary<string, (string Module, int TagCount, int ImportCount, List<string> Files)>(StringComparer.Ordinal);

        foreach (var index in indexes)
        {
            foreach (var (name, specifier) in index.Imports)
            {
                if (definedNames.Contains(name) || !char.IsUpper(name[0]))
                {
                    continue;
                }

                if (!VendorPrefixes.Any(prefix => specifier == prefix || specifier.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                var entry = vendorUsage.TryGetValue(name, out var existing)
                    ? existing
                    : (Module: specifier, TagCount: 0, ImportCount: 0, Files: []);

                entry.TagCount += index.JsxTags.GetValueOrDefault(name);
                entry.ImportCount++;

                if (entry.Files.Count < MaxUsageFiles)
                {
                    entry.Files.Add(index.RelativePath);
                }

                vendorUsage[name] = entry;
            }
        }

        foreach (var (name, usage) in vendorUsage)
        {
            components.Add(new DsComponentEntry
            {
                Name = name,
                TargetIndex = targetIndex,
                Framework = DsFrameworks.React,
                Classification = DsClassifications.PureVendor,
                VendorModule = usage.Module,
                Usage = new DsUsage { ImportCount = usage.ImportCount, TagCount = usage.TagCount, Files = usage.Files },
            });
        }

        var duplicateGroups = byName
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => new DsDuplicateGroup
            {
                Name = pair.Key,
                Files = [.. pair.Value.Select(d => d.RelativePath)],
            })
            .ToList();

        components.Sort((a, b) =>
            (b.Usage.TagCount + (b.Usage.ImportCount * 2)).CompareTo(a.Usage.TagCount + (a.Usage.ImportCount * 2)));

        return new InventoryResult(components, duplicateGroups);
    }

    /// <summary>Balanced-brace block starting at the first <c>{</c> after the position.</summary>
    private static string? BraceBlock(string text, int from)
    {
        var start = text.IndexOf('{', from);

        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var limit = Math.Min(text.Length, start + 20000);

        for (var i = start; i < limit; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return text[(start + 1)..i];
                }
            }
        }

        return null;
    }

    private static List<DsPropEntry> ParsePropsBlock(string block)
    {
        var props = new List<DsPropEntry>();

        foreach (Match match in PropsMemberPattern().Matches(block))
        {
            if (props.Count >= MaxProps)
            {
                break;
            }

            props.Add(new DsPropEntry
            {
                Name = match.Groups[1].Value,
                Required = !match.Groups[2].Success,
                Type = match.Groups[3].Value.Trim(),
            });
        }

        return props;
    }

    private static void MergeDefaults(List<DsPropEntry> props, string source)
    {
        foreach (Match match in DefaultValuePattern().Matches(source))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            var existing = props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

            if (existing is not null)
            {
                existing.DefaultValue = value;
            }
            else
            {
                props.Add(new DsPropEntry { Name = name, Required = false, DefaultValue = value });
            }
        }
    }

    private static List<DsPropEntry> ExtractProps(string text, string name)
    {
        var props = new List<DsPropEntry>();

        var declaration = Regex.Match(text, $@"(?:interface|type)\s+{Regex.Escape(name)}Props\b[^{{=]*[{{=]");

        if (declaration.Success && BraceBlock(text, declaration.Index) is { } block)
        {
            props = ParsePropsBlock(block);
        }

        var signature = Regex.Match(
            text,
            $@"(?:function\s+{Regex.Escape(name)}|const\s+{Regex.Escape(name)}[^=]*=[^(]*)\(\s*\{{([^}}]*)\}}");

        if (signature.Success)
        {
            MergeDefaults(props, signature.Groups[1].Value);
        }

        return props;
    }

    private static bool LooksLikeJsxFile(string text, string extension) =>
        extension is ".tsx" or ".jsx" || JsxTagProbe().IsMatch(text);

    /// <summary>Router entry files are pages, not portable components — the Astro <c>src/pages</c> rule applied to Next routers.</summary>
    private static bool IsRouteEntryFile(string relativePath) =>
        RouteDirectoryPattern().IsMatch(relativePath) && RouteEntryFilePattern().IsMatch(relativePath);

    private static List<Definition> ExtractReactDefinitions(string text, string relativePath, string extension)
    {
        if (!LooksLikeJsxFile(text, extension) || IsRouteEntryFile(relativePath))
        {
            return [];
        }

        var definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);

        foreach (Match match in ExportDefaultFunctionPattern().Matches(text)) Add(match.Groups[1].Value, "default");

        foreach (Match match in ExportNamedFunctionPattern().Matches(text)) Add(match.Groups[1].Value, "named");

        foreach (Match match in ExportConstComponentPattern().Matches(text)) Add(match.Groups[1].Value, "named");

        var defaultIdentifier = ExportDefaultIdentifierPattern().Match(text);

        if (defaultIdentifier.Success)
        {
            var name = defaultIdentifier.Groups[1].Value;

            if (Regex.IsMatch(text, $@"(?:function|const|class)\s+{Regex.Escape(name)}\b"))
            {
                Add(name, "default");
            }
        }

        foreach (Match match in ExportListPattern().Matches(text))
        {
            foreach (var part in match.Groups[1].Value.Split(','))
            {
                var name = part.Split(" as ")[0].Trim();

                if (name.Length > 0
                    && ComponentNamePattern().IsMatch(name)
                    && Regex.IsMatch(text, $@"(?:function|const|class)\s+{Regex.Escape(name)}\b"))
                {
                    Add(name, "named");
                }
            }
        }

        return [.. definitions.Values];

        void Add(string name, string exportKind)
        {
            if (!char.IsUpper(name[0]) || definitions.ContainsKey(name))
            {
                return;
            }

            definitions[name] = new Definition
            {
                Name = name,
                RelativePath = relativePath,
                Framework = DsFrameworks.React,
                ExportKind = exportKind,
                Props = ExtractProps(text, name),
                VendorImports = ExtractVendorImports(text),
            };
        }
    }

    private static List<Definition> ExtractAngularDefinitions(string text, string relativePath)
    {
        if (!text.Contains("@Component", StringComparison.Ordinal))
        {
            return [];
        }

        var definitions = new List<Definition>();

        foreach (Match match in AngularComponentPattern().Matches(text))
        {
            var metadata = BraceBlock(text, match.Index) ?? string.Empty;
            var selector = AngularSelectorPattern().Match(metadata) is { Success: true } selectorMatch
                ? selectorMatch.Groups[1].Value
                : null;

            var declaration = ExportClassPattern().Match(text[match.Index..]);

            if (!declaration.Success)
            {
                continue;
            }

            var props = new List<DsPropEntry>();

            foreach (Match input in AngularInputPattern().Matches(text))
            {
                if (props.Count >= MaxProps)
                {
                    break;
                }

                props.Add(new DsPropEntry
                {
                    Name = input.Groups[1].Value,
                    Required = input.Groups[2].Value == "!",
                    Type = input.Groups[3].Success ? input.Groups[3].Value.Trim() : null,
                });
            }

            definitions.Add(new Definition
            {
                Name = declaration.Groups[1].Value,
                RelativePath = relativePath,
                Framework = DsFrameworks.Angular,
                ExportKind = "named",
                Props = props,
                VendorImports = ExtractVendorImports(text),
                ElementTag = selector,
            });
        }

        return definitions;
    }

    private static List<Definition> ExtractWebComponentDefinitions(string text, string relativePath)
    {
        var definitions = new List<Definition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in CustomElementsDefinePattern().Matches(text))
        {
            seen.Add(match.Groups[2].Value);
            definitions.Add(new Definition
            {
                Name = match.Groups[2].Value,
                RelativePath = relativePath,
                Framework = DsFrameworks.WebComponents,
                ExportKind = "named",
                Props = ExtractLitProps(text),
                VendorImports = ExtractVendorImports(text),
                ElementTag = match.Groups[1].Value,
            });
        }

        foreach (Match match in CustomElementDecoratorPattern().Matches(text))
        {
            if (seen.Contains(match.Groups[2].Value))
            {
                continue;
            }

            definitions.Add(new Definition
            {
                Name = match.Groups[2].Value,
                RelativePath = relativePath,
                Framework = DsFrameworks.WebComponents,
                ExportKind = "named",
                Props = ExtractLitProps(text),
                VendorImports = ExtractVendorImports(text),
                ElementTag = match.Groups[1].Value,
            });
        }

        return definitions;
    }

    private static List<DsPropEntry> ExtractLitProps(string text)
    {
        var props = new List<DsPropEntry>();

        foreach (Match match in LitPropertyPattern().Matches(text))
        {
            if (props.Count >= MaxProps)
            {
                break;
            }

            props.Add(new DsPropEntry
            {
                Name = match.Groups[1].Value,
                Required = !match.Groups[2].Success,
                Type = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null,
            });
        }

        return props;
    }

    /// <summary>A .astro file is a component: PascalCase file stem for the name, props from <c>interface Props</c> or <c>Astro.props</c>; pages are not ported.</summary>
    private static List<Definition> ExtractAstroDefinition(string text, string relativePath)
    {
        if (AstroPagesPattern().IsMatch(relativePath))
        {
            return [];
        }

        var baseName = relativePath.Split('/').Last();
        baseName = baseName.EndsWith(".astro", StringComparison.Ordinal) ? baseName[..^6] : baseName;
        var name = NonIdentifierCharacterPattern().Replace(baseName, string.Empty);

        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return [];
        }

        var props = new List<DsPropEntry>();
        var declaration = AstroPropsPattern().Match(text);

        if (declaration.Success && BraceBlock(text, declaration.Index) is { } block)
        {
            props = ParsePropsBlock(block);
        }

        var destructure = AstroDestructurePattern().Match(text);

        if (destructure.Success)
        {
            MergeDefaults(props, destructure.Groups[1].Value);
        }

        return
        [
            new Definition
            {
                Name = name,
                RelativePath = relativePath,
                Framework = DsFrameworks.Astro,
                ExportKind = "default",
                Props = props,
                VendorImports = ExtractVendorImports(text),
            },
        ];
    }

    private static List<string> ExtractVendorImports(string text)
    {
        var found = new List<string>();

        foreach (Match match in FromModulePattern().Matches(text))
        {
            var specifier = match.Groups[1].Value;

            if (VendorPrefixes.Any(prefix => specifier == prefix || specifier.StartsWith(prefix, StringComparison.Ordinal))
                && !found.Contains(specifier, StringComparer.Ordinal))
            {
                found.Add(specifier);
            }
        }

        return found;
    }

    private static FileIndex IndexFile(string text, string relativePath)
    {
        var index = new FileIndex { RelativePath = relativePath };

        foreach (Match match in ImportStatementPattern().Matches(text))
        {
            var specifier = match.Groups[3].Value;

            if (match.Groups[1].Success)
            {
                index.Imports[match.Groups[1].Value] = specifier;
            }

            if (match.Groups[2].Success)
            {
                foreach (var part in match.Groups[2].Value.Split(','))
                {
                    var name = part.Split(" as ").Last().Trim();

                    if (name.Length > 0)
                    {
                        index.Imports[name] = specifier;
                    }
                }
            }
        }

        foreach (Match match in OpenTagPattern().Matches(text))
        {
            var tag = match.Groups[1].Value;
            var isPascal = char.IsUpper(tag[0]);
            var isElement = tag.Contains('-', StringComparison.Ordinal);

            if (!isPascal && !isElement)
            {
                continue;
            }

            var map = isPascal ? index.JsxTags : index.ElementTags;
            map[tag] = map.GetValueOrDefault(tag) + 1;

            var attributes = match.Groups[2].Value.Trim();

            if (attributes.Length == 0)
            {
                continue;
            }

            if (!index.AttributeSamples.TryGetValue(tag, out var samples))
            {
                samples = [];
                index.AttributeSamples[tag] = samples;
            }

            if (samples.Count < MaxAttributeSamplesPerTag)
            {
                samples.Add(attributes);
            }
        }

        return index;
    }

    /// <summary>Literal prop values from sampled attribute strings.</summary>
    private static Dictionary<string, List<string>> ObservedPropsFromSamples(IReadOnlyList<string> samples)
    {
        var observed = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var sample in samples)
        {
            foreach (Match match in AttributePattern().Matches(sample))
            {
                var raw = match.Groups[2].Success ? match.Groups[2].Value
                    : match.Groups[3].Success ? match.Groups[3].Value
                    : match.Groups[4].Success ? match.Groups[4].Value
                    : string.Empty;
                var value = raw.Trim('"', '\'');

                if (!observed.TryGetValue(match.Groups[1].Value, out var values))
                {
                    values = [];
                    observed[match.Groups[1].Value] = values;
                }

                if (values.Count < MaxObservedValues && !values.Contains(value, StringComparer.Ordinal))
                {
                    values.Add(value);
                }
            }
        }

        foreach (var values in observed.Values)
        {
            values.Sort(StringComparer.Ordinal);
        }

        return observed;
    }

    private static (string Classification, string? VendorModule) Classify(Definition definition)
    {
        if (ShadcnPathPattern().IsMatch(definition.RelativePath))
        {
            return (DsClassifications.Vendored, "shadcn/ui");
        }

        if (definition.VendorImports.Count > 0)
        {
            return (DsClassifications.Wrapped, definition.VendorImports[0]);
        }

        return (DsClassifications.Bespoke, null);
    }
}
