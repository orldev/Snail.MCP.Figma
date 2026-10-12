using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Extraction.Codebase;

/// <summary>Classifies the inventory into atomic-design levels, maps domain names to generic components (FollowButton → Button) and lists missing generics.</summary>
public static partial class ArchitectureClassifier
{
    public const string Atom = "atom";
    public const string Molecule = "molecule";
    public const string Organism = "organism";
    public const string Template = "template";
    public const string Unclassified = "unclassified";

    /// <summary>Generic nouns and their level.</summary>
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["button"] = Atom,
        ["icon"] = Atom,
        ["avatar"] = Atom,
        ["badge"] = Atom,
        ["chip"] = Atom,
        ["tag"] = Atom,
        ["pill"] = Atom,
        ["link"] = Atom,
        ["input"] = Atom,
        ["textarea"] = Atom,
        ["select"] = Atom,
        ["checkbox"] = Atom,
        ["radio"] = Atom,
        ["switch"] = Atom,
        ["toggle"] = Atom,
        ["slider"] = Atom,
        ["spinner"] = Atom,
        ["skeleton"] = Atom,
        ["divider"] = Atom,
        ["rule"] = Atom,
        ["label"] = Atom,
        ["tooltip"] = Atom,
        ["image"] = Atom,
        ["logo"] = Atom,

        ["card"] = Molecule,
        ["menu"] = Molecule,
        ["dropdown"] = Molecule,
        ["combobox"] = Molecule,
        ["breadcrumb"] = Molecule,
        ["breadcrumbs"] = Molecule,
        ["tabs"] = Molecule,
        ["accordion"] = Molecule,
        ["field"] = Molecule,
        ["form"] = Molecule,
        ["search"] = Molecule,
        ["pagination"] = Molecule,
        ["stepper"] = Molecule,
        ["toast"] = Molecule,
        ["snackbar"] = Molecule,
        ["alert"] = Molecule,
        ["banner"] = Molecule,
        ["dialog"] = Molecule,
        ["modal"] = Molecule,
        ["popover"] = Molecule,
        ["row"] = Molecule,
        ["listitem"] = Molecule,
        ["bar"] = Molecule,
        ["toolbar"] = Molecule,
        ["actionbar"] = Molecule,
        ["composer"] = Molecule,
        ["editor"] = Molecule,

        ["nav"] = Organism,
        ["navbar"] = Organism,
        ["sidebar"] = Organism,
        ["rail"] = Organism,
        ["header"] = Organism,
        ["footer"] = Organism,
        ["hero"] = Organism,
        ["table"] = Organism,
        ["list"] = Organism,
        ["drawer"] = Organism,
        ["gallery"] = Organism,
        ["carousel"] = Organism,
        ["slideshow"] = Organism,
        ["feed"] = Organism,
        ["wall"] = Organism,
        ["canvas"] = Organism,
        ["room"] = Organism,
        ["terminal"] = Organism,
        ["marquee"] = Organism,
        ["lightbox"] = Organism,
        ["frame"] = Organism,
        ["section"] = Organism,
        ["band"] = Organism,
        ["prose"] = Organism,
    };

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryPattern();

    [GeneratedRegex(@"page|layout|template|shell|view|screen", RegexOptions.IgnoreCase)]
    private static partial Regex TemplateNamePattern();

    public static DsArchitecture Classify(IReadOnlyList<DsComponentEntry> components)
    {
        var portable = components
            .Where(c => !string.Equals(c.Classification, DsClassifications.PureVendor, StringComparison.Ordinal))
            .ToList();
        var names = new HashSet<string>(portable.Select(c => c.Name.ToLowerInvariant()), StringComparer.Ordinal);

        var architecture = new DsArchitecture();
        var impliedGenerics = new List<string>();

        foreach (var component in portable)
        {
            var words = SplitWords(component.Name);

            string? matched = null;
            var qualifier = Array.Empty<string>();

            for (var take = Math.Min(2, words.Length); take >= 1; take--)
            {
                var suffix = string.Concat(words[^take..]);

                if (Canonical.ContainsKey(suffix))
                {
                    matched = suffix;
                    qualifier = words[..^take];
                    break;
                }
            }

            if (matched is not null)
            {
                architecture.Levels[component.Name] = Canonical[matched];

                if (qualifier.Length <= 0) continue;

                var genericName = char.ToUpperInvariant(matched[0]) + matched[1..];
                var qualifierText = string.Join(' ', qualifier);

                architecture.Specializations.Add(new DsSpecialization
                {
                    Component = component.Name,
                    GenericOf = genericName,
                    Qualifier = qualifierText,
                    Suggestion =
                        $"'{component.Name}' is a {qualifierText} APPLICATION of the generic {genericName}: " +
                        $"in a general-purpose system it becomes a variant or a recipe of {genericName}, not a component of its own.",
                });

                if (!impliedGenerics.Contains(genericName, StringComparer.Ordinal))
                {
                    impliedGenerics.Add(genericName);
                }
            }
            else
            {
                architecture.Levels[component.Name] = TemplateNamePattern().IsMatch(component.Name)
                    ? Template
                    : Unclassified;
            }
        }

        architecture.MissingPrimitives.AddRange(impliedGenerics
            .Where(generic => !names.Contains(generic.ToLowerInvariant()))
            .Order(StringComparer.Ordinal));

        foreach (var level in architecture.Levels.Values)
        {
            architecture.Summary[level] = architecture.Summary.GetValueOrDefault(level) + 1;
        }

        return architecture;
    }

    /// <summary>PascalCase or camelCase into lowercase words.</summary>
    private static string[] SplitWords(string name) =>
        WordBoundaryPattern()
            .Replace(name, "$1 $2")
            .ToLowerInvariant()
            .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
}
