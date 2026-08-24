using System.Globalization;
using System.Text.RegularExpressions;

namespace Snail.MCP.Figma.Application.Diff;

/// <summary>Who saved a version and when — from the file's version list.</summary>
public sealed record VersionMeta(
    string VersionId,
    string? Label,
    string? CreatedAt,
    string? UserHandle,
    bool IsHead = false);

/// <summary>Everything a changelog is built from.</summary>
public sealed record ChangelogInput
{
    public required string FileKey { get; init; }

    public string? FileName { get; init; }

    public required string FromVersionId { get; init; }

    public required string ToVersionId { get; init; }

    public VersionMeta? FromMeta { get; init; }

    public VersionMeta? ToMeta { get; init; }

    public required JsonObject PageStructure { get; init; }

    public IReadOnlyList<JsonObject>? ScopedNodes { get; init; }

    public IReadOnlyList<string>? Notes { get; init; }
}

/// <summary>Markdown changelog from a version diff; a pure function without I/O.</summary>
public static partial class ChangelogFormatter
{
    [GeneratedRegex(@"([\\`*_{}\[\]<>])")]
    private static partial Regex MarkdownSpecialPattern();

    public static string Render(ChangelogInput input, string mode = DiffModes.Standard)
    {
        var lines = new List<string>();
        var title = string.IsNullOrWhiteSpace(input.FileName)
            ? "Figma file changelog"
            : $"{input.FileName} — changelog";

        lines.Add($"# {title}");
        lines.Add(string.Empty);
        lines.Add($"**From:** {FormatVersionRef(input.FromMeta, input.FromVersionId)}");
        lines.Add($"**To:** {FormatVersionRef(input.ToMeta, input.ToVersionId)}");

        if (SpanDays(input.FromMeta?.CreatedAt, input.ToMeta?.CreatedAt) is { } days)
        {
            lines.Add($"**Days apart:** {days}");
        }

        lines.Add(string.Empty);

        if (mode == DiffModes.Summary)
        {
            lines.Add(SummaryLine(input));
            lines.Add(string.Empty);
            AppendNotes(lines, input.Notes);

            return Finish(lines);
        }

        AppendPageStructure(lines, input.PageStructure);

        if (input.ScopedNodes is { Count: > 0 })
        {
            AppendComponents(lines, input.ScopedNodes, mode);
        }
        else
        {
            lines.Add("## Components");
            lines.Add(string.Empty);
            lines.Add("_No component fell into the comparison scope. Pass componentIds to see the per-component changes._");
            lines.Add(string.Empty);
        }

        AppendNotes(lines, input.Notes);

        return Finish(lines);
    }

    private static string Finish(IEnumerable<string> lines) =>
        $"{string.Join('\n', lines).TrimEnd()}\n";

    private static void AppendPageStructure(List<string> lines, JsonObject pageStructure)
    {
        var summary = pageStructure["summary"];
        var total = (summary?["added"]?.GetValue<int>() ?? 0)
            + (summary?["removed"]?.GetValue<int>() ?? 0)
            + (summary?["renamed"]?.GetValue<int>() ?? 0);

        lines.Add("## Page structure");
        lines.Add(string.Empty);

        if (total == 0)
        {
            lines.Add("_Nothing changed at the page level._");
            lines.Add(string.Empty);

            return;
        }

        AppendPageList(lines, pageStructure["pages_added"] as JsonArray, "Added");
        AppendPageList(lines, pageStructure["pages_removed"] as JsonArray, "Removed");

        if (pageStructure["pages_renamed"] is JsonArray renamed && renamed.Count > 0)
        {
            lines.Add($"**Renamed ({renamed.Count}):**");

            foreach (var page in renamed)
            {
                var id = page?["id"]?.GetValue<string>() ?? string.Empty;
                var oldName = Escape(page?["old_name"]?.GetValue<string>() ?? string.Empty);
                var newName = Escape(page?["new_name"]?.GetValue<string>() ?? string.Empty);
                lines.Add($"- `{id}`: {oldName} → {newName}");
            }

            lines.Add(string.Empty);
        }
    }

    private static void AppendPageList(List<string> lines, JsonArray? pages, string heading)
    {
        if (pages is null || pages.Count == 0)
        {
            return;
        }

        lines.Add($"**{heading} ({pages.Count}):**");

        foreach (var page in pages)
        {
            var name = Escape(page?["name"]?.GetValue<string>() ?? string.Empty);
            var id = page?["id"]?.GetValue<string>() ?? string.Empty;
            lines.Add($"- {name} `{id}`");
        }

        lines.Add(string.Empty);
    }

    private static void AppendComponents(List<string> lines, IReadOnlyList<JsonObject> scoped, string mode)
    {
        lines.Add("## Components");
        lines.Add(string.Empty);

        var changed = scoped.Where(node => ChangeCount(node) > 0).ToList();
        var unchanged = scoped.Where(node => ChangeCount(node) == 0 && NotesOf(node).Count == 0).ToList();
        var notFound = scoped
            .Where(node => NotesOf(node).Any(note => note.Contains("was not found", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (changed.Count == 0 && unchanged.Count == 0 && notFound.Count == 0)
        {
            lines.Add("_None of the selected components changed._");
            lines.Add(string.Empty);

            return;
        }

        foreach (var node in changed)
        {
            AppendComponentBlock(lines, node, mode);
        }

        if (unchanged.Count > 0)
        {
            var names = unchanged.Select(node => $"`{NameOf(node)}`");
            lines.Add($"**Unchanged:** {string.Join(", ", names)}");
            lines.Add(string.Empty);
        }

        if (notFound.Count <= 0) return;
        {
            var ids = notFound.Select(node => $"`{node["node_id"]?.GetValue<string>() ?? "(empty)"}`");
            lines.Add($"**Not found in either version:** {string.Join(", ", ids)}");
            lines.Add(string.Empty);
        }
    }

    private static void AppendComponentBlock(List<string> lines, JsonObject node, string mode)
    {
        var name = node["node_name"]?.GetValue<string>();
        var heading = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
        var count = ChangeCount(node);

        lines.Add($"### {Escape(heading)} — `{node["node_id"]?.GetValue<string>()}`");
        lines.Add($"**Changes: {count}**");

        var bullets = new List<string>();

        if (node["name_changed"] is JsonObject renamed)
        {
            var from = Escape(renamed["from"]?.GetValue<string>() ?? string.Empty);
            var to = Escape(renamed["to"]?.GetValue<string>() ?? string.Empty);
            bullets.Add($"- Renamed: `{from}` → `{to}`");
        }

        if (node["description_changed"] is JsonObject description)
        {
            var fromLength = description["from"]?.GetValue<string>()?.Length ?? 0;
            var toLength = description["to"]?.GetValue<string>()?.Length ?? 0;
            bullets.Add($"- Description changed (length {fromLength} → {toLength})");
        }

        AppendChildrenBullet(bullets, node["children_added"] as JsonArray, "Children added");
        AppendChildrenBullet(bullets, node["children_removed"] as JsonArray, "Children removed");

        if (bullets.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(bullets);
        }

        AppendPropertyChanges(lines, node["component_properties"] as JsonObject, mode);
        AppendBindingChanges(lines, node["binding_changes"] as JsonArray, mode);
        AppendMetadataChanges(lines, node["metadata_changes"] as JsonArray);

        lines.Add(string.Empty);
    }

    private static void AppendChildrenBullet(List<string> bullets, JsonArray? children, string heading)
    {
        if (children is null || children.Count == 0)
        {
            return;
        }

        var inline = children.Select(child =>
        {
            var name = child?["name"]?.GetValue<string>();

            return $"`{Escape(string.IsNullOrWhiteSpace(name) ? child?["id"]?.GetValue<string>() ?? string.Empty : name)}`";
        });

        bullets.Add($"- {heading} ({children.Count}): {string.Join(", ", inline)}");
    }

    private static void AppendPropertyChanges(List<string> lines, JsonObject? properties, string mode)
    {
        if (properties?["summary"] is not JsonObject summary)
        {
            return;
        }

        var parts = new List<string>();

        void Part(string key, string text)
        {
            var value = summary[key]?.GetValue<int>() ?? 0;

            if (value > 0)
            {
                parts.Add($"{value} {text}");
            }
        }

        Part("added", "added");
        Part("removed", "removed");
        Part("type_changed", "changed type");
        Part("default_changed", "changed default");

        if (parts.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"**Component properties:** {string.Join(", ", parts)}");

        if (mode != DiffModes.Detailed)
        {
            return;
        }

        foreach (var property in properties["added"] as JsonArray ?? [])
        {
            lines.Add($"- ➕ `{Escape(property?["name"]?.GetValue<string>() ?? string.Empty)}` " +
                      $"({property?["type"]?.GetValue<string>()}, default: `{Stringify(property?["default_value"])}`)");
        }

        foreach (var property in properties["removed"] as JsonArray ?? [])
        {
            lines.Add($"- ➖ `{Escape(property?["name"]?.GetValue<string>() ?? string.Empty)}` ({property?["type"]?.GetValue<string>()})");
        }

        foreach (var property in properties["type_changed"] as JsonArray ?? [])
        {
            lines.Add($"- 🔄 `{Escape(property?["name"]?.GetValue<string>() ?? string.Empty)}`: " +
                      $"{property?["from_type"]?.GetValue<string>()} → {property?["to_type"]?.GetValue<string>()}");
        }

        foreach (var property in properties["default_changed"] as JsonArray ?? [])
        {
            lines.Add($"- ⚙️ `{Escape(property?["name"]?.GetValue<string>() ?? string.Empty)}`: " +
                      $"`{Stringify(property?["from_default"])}` → `{Stringify(property?["to_default"])}`");
        }
    }

    private static void AppendBindingChanges(List<string> lines, JsonArray? bindings, string mode)
    {
        if (bindings is null || bindings.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"**Variable bindings ({bindings.Count}):**");

        if (mode == DiffModes.Detailed)
        {
            foreach (var binding in bindings)
            {
                var kind = binding?["change_kind"]?.GetValue<string>();
                var from = binding?["from_variable_id"]?.GetValue<string>();
                var to = binding?["to_variable_id"]?.GetValue<string>();

                var arrow = kind switch
                {
                    "added" => $"→ `{to}`",
                    "removed" => $"(removed, was `{from}`)",
                    _ => $"`{from}` → `{to}`",
                };

                var name = binding?["node_name"]?.GetValue<string>();
                var label = string.IsNullOrWhiteSpace(name) ? binding?["node_id"]?.GetValue<string>() ?? string.Empty : name;
                lines.Add($"- {Escape(label)} — `{binding?["property"]?.GetValue<string>()}` {arrow}");
            }

            return;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            var kind = binding?["change_kind"]?.GetValue<string>() ?? "rebound";
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        var parts = new List<string>();

        if (counts.GetValueOrDefault("added") > 0) parts.Add($"{counts["added"]} added");
        if (counts.GetValueOrDefault("removed") > 0) parts.Add($"{counts["removed"]} removed");
        if (counts.GetValueOrDefault("rebound") > 0) parts.Add($"{counts["rebound"]} rebound");

        lines.Add($"_{string.Join(", ", parts)}._");
    }

    private static void AppendMetadataChanges(List<string> lines, JsonArray? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"**Description and annotation edits ({changes.Count}, from the plugin buffer):**");

        foreach (var change in changes)
        {
            var field = change?["field"]?.GetValue<string>();
            var label = field == "annotations" ? "annotations" : "description";
            lines.Add($"- {label} changed");
        }
    }

    private static void AppendNotes(List<string> lines, IReadOnlyList<string>? notes)
    {
        if (notes is null || notes.Count == 0)
        {
            return;
        }

        lines.Add("## Caveats");
        lines.Add(string.Empty);

        foreach (var note in notes)
        {
            lines.Add($"- {note}");
        }

        lines.Add(string.Empty);
    }

    private static string SummaryLine(ChangelogInput input)
    {
        var summary = input.PageStructure["summary"];
        var parts = new List<string>();

        void Pages(string key, string verb)
        {
            var value = summary?[key]?.GetValue<int>() ?? 0;

            if (value > 0)
            {
                parts.Add($"Pages {verb}: {value}");
            }
        }

        Pages("added", "added");
        Pages("removed", "removed");
        Pages("renamed", "renamed");

        var withChanges = input.ScopedNodes?.Count(node => ChangeCount(node) > 0) ?? 0;
        var totalChanges = input.ScopedNodes?.Sum(ChangeCount) ?? 0;

        if (withChanges > 0)
        {
            parts.Add($"Components changed: {withChanges}");
            parts.Add($"Changes: {totalChanges}");
        }

        return parts.Count == 0 ? "_No structural changes were found._" : $"{string.Join("; ", parts)}.";
    }

    private static string FormatVersionRef(VersionMeta? meta, string versionId)
    {
        if (meta?.IsHead == true || versionId == "current")
        {
            var date = meta?.CreatedAt is { } created ? FormatDate(created) : "the current state";
            var user = string.IsNullOrWhiteSpace(meta?.UserHandle) ? string.Empty : $", by {meta!.UserHandle}";

            return $"Current state (last edit {date}{user})";
        }

        if (meta is null)
        {
            return $"`{versionId}` _(metadata unavailable)_";
        }

        var label = string.IsNullOrWhiteSpace(meta.Label) ? "_(untitled)_" : $"'{meta.Label}'";
        var parts = new List<string> { label };

        if (meta.CreatedAt is { } createdAt) parts.Add(FormatDate(createdAt));
        if (!string.IsNullOrWhiteSpace(meta.UserHandle)) parts.Add($"by {meta.UserHandle}");

        return $"{string.Join(" — ", parts)} `{versionId}`";
    }

    private static int? SpanDays(string? fromIso, string? toIso)
    {
        if (!DateTimeOffset.TryParse(fromIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(toIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
        {
            return null;
        }

        return Math.Max(0, (int)Math.Round((to - from).TotalDays));
    }

    private static string FormatDate(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : iso;

    /// <summary>Escapes what breaks Markdown, lightly — release notes must stay readable.</summary>
    private static string Escape(string value) => MarkdownSpecialPattern().Replace(value, "\\$1");

    private static string Stringify(JsonNode? value) => value switch
    {
        null => "?",
        JsonValue json when json.TryGetValue<string>(out var text) => text,
        _ => value.ToJsonString(),
    };

    private static int ChangeCount(JsonObject node) => node["change_count"]?.GetValue<int>() ?? 0;

    private static string NameOf(JsonObject node)
    {
        var name = node["node_name"]?.GetValue<string>();

        return string.IsNullOrWhiteSpace(name) ? node["node_id"]?.GetValue<string>() ?? string.Empty : name;
    }

    private static List<string> NotesOf(JsonObject node) =>
        (node["notes"] as JsonArray ?? [])
            .Select(note => note?.GetValue<string>() ?? string.Empty)
            .ToList();
}
