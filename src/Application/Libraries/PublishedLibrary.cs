namespace Snail.MCP.Figma.Application.Libraries;

/// <summary>The catalog of one library file as Figma publishes it: every component set and component with the key a plugin imports it by.</summary>
/// <remarks>
/// <para>The plugin cannot list a library: Figma's Plugin API offers library variables, but components and styles only by a key already known. The catalog therefore comes from REST, which answers for any file the token can view.</para>
/// <para>Variants are left out unless asked for: a kit of a hundred sets carries thousands of them, and a set's key is enough to place its default variant and switch the rest by properties.</para>
/// </remarks>
public sealed class PublishedLibrary(IPublishedDocumentSource api)
{
    /// <summary>The page size a call gets when it names none.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page a call may ask for.</summary>
    public const int MaxLimit = 500;

    /// <summary>The main file key a link or a bare key names; null when it names none.</summary>
    /// <remarks>A branch link answers its main file: Figma publishes from the main file only, and asked for a branch it lists nothing.</remarks>
    public static string? FileKeyOf(string? linkOrKey)
    {
        if (DocumentLocator.Parse(linkOrKey) is { } reference)
        {
            return reference.Key;
        }

        var trimmed = linkOrKey?.Trim() ?? string.Empty;

        return trimmed.Length >= 10 && trimmed.All(char.IsAsciiLetterOrDigit) ? trimmed : null;
    }

    /// <summary>Reads the published component sets and components of the requested file and answers one page of those matching the query.</summary>
    /// <remarks>Figma answers both lists whole, without pages of its own; the page is cut here, so a next page costs the same two requests.</remarks>
    /// <exception cref="PublishedSourceException">Figma refused or did not answer.</exception>
    public async Task<LibraryPage> ReadAsync(LibraryQuery request, CancellationToken cancellationToken)
    {
        var setsTask = api.GetComponentSetsAsync(request.FileKey, cancellationToken);
        var componentsTask = api.GetComponentsAsync(request.FileKey, cancellationToken);
        var sets = await setsTask.ConfigureAwait(false);
        var components = await componentsTask.ConfigureAwait(false);

        return Catalog(request, sets, components);
    }

    /// <summary>The page of the catalog two REST answers make: sets first, then standalone components, then, when asked for, variants under their set.</summary>
    public static LibraryPage Catalog(LibraryQuery request, JsonNode sets, JsonNode components)
    {
        var setEntries = Published(sets, "component_sets");
        var componentEntries = Published(components, "components");
        var variantsOfSet = componentEntries
            .Where(entry => entry.SetNodeId is not null)
            .GroupBy(entry => entry.SetNodeId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(variant => variant with { Kind = LibraryEntry.Variant }).ToList(), StringComparer.Ordinal);

        var catalog = new List<LibraryEntry>();

        foreach (var set in setEntries)
        {
            var variants = variantsOfSet.GetValueOrDefault(set.NodeId) ?? [];
            catalog.Add(set with { Kind = LibraryEntry.ComponentSet, Variants = variants.Count });

            if (request.ShouldListVariants)
            {
                catalog.AddRange(variants);
            }

            variantsOfSet.Remove(set.NodeId);
        }

        foreach (var component in componentEntries.Where(entry => entry.SetNodeId is null))
        {
            catalog.Add(component);
        }

        if (request.ShouldListVariants)
        {
            catalog.AddRange(variantsOfSet.Values.SelectMany(orphans => orphans));
        }

        var matched = catalog.Where(entry => entry.Matches(request.Query)).ToList();
        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var offset = Math.Max(0, request.Offset);
        var page = matched.Skip(offset).Take(limit).ToList();
        int? next = offset + page.Count < matched.Count ? offset + page.Count : null;

        return new LibraryPage(request.FileKey, page, matched.Count, setEntries.Count, componentEntries.Count, offset, next);
    }

    private static List<LibraryEntry> Published(JsonNode response, string kind)
    {
        var list = response["meta"]?[kind] as JsonArray ?? response[kind] as JsonArray ?? [];
        var entries = new List<LibraryEntry>();

        foreach (var item in list)
        {
            var key = item?["key"]?.GetValue<string>();
            var nodeId = item?["node_id"]?.GetValue<string>();

            if (key is null || nodeId is null)
            {
                continue;
            }

            var frame = item?["containing_frame"];
            var set = frame?["containingComponentSet"] ?? frame?["containingStateGroup"];
            var description = item?["description"]?.GetValue<string>();

            entries.Add(new LibraryEntry(
                LibraryEntry.Component,
                key,
                item?["name"]?.GetValue<string>() ?? string.Empty,
                string.IsNullOrWhiteSpace(description) ? null : description,
                nodeId,
                frame?["pageName"]?.GetValue<string>(),
                set?["name"]?.GetValue<string>(),
                set?["nodeId"]?.GetValue<string>()));
        }

        return entries;
    }
}

/// <summary>What a catalog call asks for: the library file, a filter and a page.</summary>
/// <param name="FileKey">The main file key of the library: Figma publishes from the main file, never from a branch.</param>
/// <param name="Query">A substring of the name, description, page or set; empty matches everything.</param>
/// <param name="ShouldListVariants">Whether the variants of every set are listed too, each under its set.</param>
/// <param name="Offset">How many matches to skip.</param>
/// <param name="Limit">How many matches to answer.</param>
public sealed record LibraryQuery(string FileKey, string? Query, bool ShouldListVariants, int Offset, int Limit);

/// <summary>One component set, standalone component or variant of a library.</summary>
/// <param name="Kind"><see cref="ComponentSet"/>, <see cref="Component"/> or <see cref="Variant"/>.</param>
/// <param name="Key">What figma_instantiate_component imports it by; a set's key places its default variant.</param>
/// <param name="Name">Its name; a variant's is its property values, such as <c>Size=Large, State=Hover</c>.</param>
/// <param name="Description">What the publisher wrote about it, when anything.</param>
/// <param name="NodeId">Its node in the library file.</param>
/// <param name="Page">The page of the library file it sits on.</param>
/// <param name="SetName">The set a variant belongs to.</param>
/// <param name="SetNodeId">The node of that set.</param>
/// <param name="Variants">How many variants a set publishes.</param>
public sealed record LibraryEntry(
    string Kind,
    string Key,
    string Name,
    string? Description,
    string NodeId,
    string? Page,
    string? SetName,
    string? SetNodeId,
    int? Variants = null)
{
    /// <summary>A component set: its key places the default variant.</summary>
    public const string ComponentSet = "COMPONENT_SET";

    /// <summary>A component outside any set.</summary>
    public const string Component = "COMPONENT";

    /// <summary>One variant of a set.</summary>
    public const string Variant = "VARIANT";

    /// <summary>Whether <paramref name="query"/> is found in the name, description, page or set, ignoring case; an empty query matches.</summary>
    public bool Matches(string? query) =>
        string.IsNullOrWhiteSpace(query)
        || Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (Page?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || (SetName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>The entry as the tool answers it, without the fields it has no value for.</summary>
    public JsonObject Describe()
    {
        var described = new JsonObject
        {
            ["kind"] = Kind,
            ["key"] = Key,
            ["name"] = Name,
            ["nodeId"] = NodeId,
        };

        AddWhenSet(described, "description", Description);
        AddWhenSet(described, "page", Page);
        AddWhenSet(described, "set", Kind == Variant ? SetName : null);

        if (Variants is { } count)
        {
            described["variants"] = count;
        }

        return described;
    }

    private static void AddWhenSet(JsonObject target, string name, string? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }
}

/// <summary>One page of a library's catalog and where the next one starts.</summary>
/// <param name="FileKey">The library file read.</param>
/// <param name="Entries">The entries of this page.</param>
/// <param name="Matched">How many entries matched across all pages.</param>
/// <param name="PublishedSets">How many component sets the file publishes.</param>
/// <param name="PublishedComponents">How many components it publishes, variants included.</param>
/// <param name="Offset">Where this page starts among the matches.</param>
/// <param name="NextOffset">Where the next page starts; null on the last one.</param>
public sealed record LibraryPage(
    string FileKey,
    IReadOnlyList<LibraryEntry> Entries,
    int Matched,
    int PublishedSets,
    int PublishedComponents,
    int Offset,
    int? NextOffset)
{
    /// <summary>Whether the file publishes nothing at all, which is what an unpublished file or a branch looks like.</summary>
    public bool IsEmptyLibrary => PublishedSets == 0 && PublishedComponents == 0;

    /// <summary>The page as the tool answers it.</summary>
    public JsonObject Describe() => new()
    {
        ["source"] = "library",
        ["fileKey"] = FileKey,
        ["publishedSets"] = PublishedSets,
        ["publishedComponents"] = PublishedComponents,
        ["matched"] = Matched,
        ["offset"] = Offset,
        ["returned"] = Entries.Count,
        ["items"] = new JsonArray([.. Entries.Select(entry => (JsonNode)entry.Describe())]),
        ["nextOffset"] = NextOffset,
    };
}
