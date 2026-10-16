using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Package name and all its dependencies in one flat map.</summary>
public sealed class PackageInfo
{
    /// <summary>The <c>name</c> field of <c>package.json</c>; <c>null</c> when the file is missing or names nothing.</summary>
    public string? Name { get; init; }

    /// <summary>Versions from the dependencies, devDependencies and peerDependencies sections, keyed by package name.</summary>
    public Dictionary<string, string> Dependencies { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Whether any dependency section lists the package.</summary>
    public bool Has(string dependency) => Dependencies.ContainsKey(dependency);
}

/// <summary>Detects framework, styling method and vendor layers from package.json and files — no AST, no code execution.</summary>
public static partial class StackDetectors
{
    [GeneratedRegex(@"^[\^~]?4\.")]
    private static partial Regex TailwindV4Version();

    [GeneratedRegex(@"\.module\.(css|scss|sass)$", RegexOptions.IgnoreCase)]
    private static partial Regex CssModulePattern();

    [GeneratedRegex(@"@theme\b")]
    private static partial Regex TailwindThemeAtRule();

    /// <summary>Vendor component layers recognized by dependency name.</summary>
    private static readonly (string Dependency, string Display)[] ComponentLibraryDeps =
    [
        ("@mui/material", "Material UI (MUI)"),
        ("@mui/joy", "MUI Joy"),
        ("@material/web", "Material Web Components"),
        ("@angular/material", "Angular Material"),
        ("@chakra-ui/react", "Chakra UI"),
        ("antd", "Ant Design"),
        ("@mantine/core", "Mantine"),
        ("react-bootstrap", "React Bootstrap"),
        ("bootstrap", "Bootstrap"),
        ("@headlessui/react", "Headless UI"),
        ("@ariakit/react", "Ariakit"),
        ("react-aria-components", "React Aria Components"),
        ("@shoelace-style/shoelace", "Shoelace"),
        ("@spectrum-web-components/base", "Spectrum Web Components"),
        ("daisyui", "daisyUI"),
    ];

    /// <summary>The package name and dependencies from <c>package.json</c> under <c>root</c>; empty when the file is missing or unreadable.</summary>
    public static PackageInfo ReadPackageInfo(string root)
    {
        var raw = CodeWalker.ReadText(Path.Combine(root, "package.json"));

        if (raw is null)
        {
            return new PackageInfo();
        }

        try
        {
            if (JsonNode.Parse(raw) is not JsonObject package)
            {
                return new PackageInfo();
            }

            var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var section in (string[])["dependencies", "devDependencies", "peerDependencies"])
            {
                if (package[section] is not JsonObject block)
                {
                    continue;
                }

                foreach (var (name, version) in block)
                {
                    dependencies[name] = version?.ToString() ?? string.Empty;
                }
            }

            return new PackageInfo
            {
                Name = package["name"]?.GetValue<string>(),
                Dependencies = dependencies,
            };
        }
        catch (JsonException)
        {
            return new PackageInfo();
        }
    }

    /// <summary>The frameworks the dependencies point to, falling back to file extensions when none do, or only <see cref="DsFrameworks.Unknown"/> when neither does.</summary>
    public static List<string> DetectFrameworks(PackageInfo package, IReadOnlyList<WalkedFile> files)
    {
        var found = new List<string>();

        void Add(string framework)
        {
            if (!found.Contains(framework, StringComparer.Ordinal))
            {
                found.Add(framework);
            }
        }

        if (package.Has("astro")) Add(DsFrameworks.Astro);
        if (package.Has("next")) Add(DsFrameworks.Next);
        if (package.Has("react") || package.Has("react-dom")) Add(DsFrameworks.React);
        if (package.Has("@angular/core")) Add(DsFrameworks.Angular);
        if (package.Has("lit") || package.Has("lit-element") || package.Has("@stencil/core")) Add(DsFrameworks.WebComponents);
        if (package.Has("vue") || package.Has("nuxt")) Add(DsFrameworks.Vue);
        if (package.Has("svelte")) Add(DsFrameworks.Svelte);

        if (found.Count == 0)
        {
            if (files.Any(f => f.Extension == ".astro")) Add(DsFrameworks.Astro);
            if (files.Any(f => f.Extension is ".tsx" or ".jsx")) Add(DsFrameworks.React);
            if (files.Any(f => f.RelativePath.EndsWith(".component.ts", StringComparison.Ordinal))) Add(DsFrameworks.Angular);
            if (files.Any(f => f.Extension == ".vue")) Add(DsFrameworks.Vue);
            if (files.Any(f => f.Extension == ".svelte")) Add(DsFrameworks.Svelte);
        }

        return found.Count > 0 ? found : [DsFrameworks.Unknown];
    }

    /// <summary>The styling methods the dependencies and style files point to, values of <see cref="DsStylingMethods"/>.</summary>
    public static List<string> DetectStylingMethods(PackageInfo package, IReadOnlyList<WalkedFile> files)
    {
        var found = new List<string>();

        void Add(string method)
        {
            if (!found.Contains(method, StringComparer.Ordinal))
            {
                found.Add(method);
            }
        }

        if (package.Has("tailwindcss"))
        {
            var version = package.Dependencies.GetValueOrDefault("tailwindcss", string.Empty);

            var v4ByVersion = TailwindV4Version().IsMatch(version);
            var v4ByCss = files.Any(file =>
            {
                if (file.Extension != ".css")
                {
                    return false;
                }

                var text = CodeWalker.ReadText(file.Path);

                return text is not null
                    && (text.Contains("@import \"tailwindcss\"", StringComparison.Ordinal)
                        || text.Contains("@import 'tailwindcss'", StringComparison.Ordinal)
                        || TailwindThemeAtRule().IsMatch(text));
            });

            Add(v4ByVersion || v4ByCss ? DsStylingMethods.TailwindV4 : DsStylingMethods.TailwindV3);
        }

        if (files.Any(f => CssModulePattern().IsMatch(f.RelativePath))) Add(DsStylingMethods.CssModules);
        if (files.Any(f => f.Extension is ".scss" or ".sass") || package.Has("sass")) Add(DsStylingMethods.Scss);
        if (package.Has("@emotion/react") || package.Has("@emotion/styled")) Add(DsStylingMethods.Emotion);
        if (package.Has("styled-components")) Add(DsStylingMethods.StyledComponents);
        if (files.Any(f => f.Extension == ".css" && !CssModulePattern().IsMatch(f.RelativePath))) Add(DsStylingMethods.VanillaCss);

        return found;
    }

    /// <summary>The vendor component libraries found through their configuration files, directories or dependencies.</summary>
    public static List<DsComponentLibrary> DetectComponentLibraries(string root, PackageInfo package)
    {
        var libraries = new List<DsComponentLibrary>();

        var shadcnConfig = new[] { "components.json", "src/components.json" }
            .FirstOrDefault(p => File.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))));
        var shadcnDirectory = new[] { "components/ui", "src/components/ui", "app/components/ui" }
            .FirstOrDefault(p => Directory.Exists(Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar))));

        if (shadcnConfig is not null || shadcnDirectory is not null)
        {
            libraries.Add(new DsComponentLibrary
            {
                Name = "shadcn/ui",
                Kind = "vendored",
                Evidence = [.. new[] { shadcnConfig, shadcnDirectory }.Where(x => x is not null).Select(x => x!)],
            });
        }

        var radix = package.Dependencies.Keys
            .Where(d => d.StartsWith("@radix-ui/", StringComparison.Ordinal))
            .Take(8)
            .ToList();

        if (radix.Count > 0)
        {
            libraries.Add(new DsComponentLibrary { Name = "Radix UI", Kind = "package", Evidence = radix });
        }

        foreach (var (dependency, display) in ComponentLibraryDeps)
        {
            if (package.Has(dependency))
            {
                libraries.Add(new DsComponentLibrary { Name = display, Kind = "package", Evidence = [dependency] });
            }
        }

        if (package.Has("class-variance-authority"))
        {
            libraries.Add(new DsComponentLibrary
            {
                Name = "CVA (class-variance-authority)",
                Kind = "package",
                Evidence = ["class-variance-authority"],
            });
        }

        return libraries;
    }
}
