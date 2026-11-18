using System.Globalization;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// One record's change history from <c>GET /api/transactions/history/objects/{class}/{handle}</c>: each change with
/// its time, user and transaction, and the fields it changed, found by comparing the record as stored before and
/// after it (raw Gramps JSON, as Gramps Web keeps it in its transaction history).
/// </summary>
public static class RecordHistoryFormatter
{
    /// <summary>Field lines shown per change; the others are counted.</summary>
    internal const int MaxFieldLinesPerChange = 12;

    /// <summary>Characters of a value shown before it is cut.</summary>
    internal const int MaxValueChars = 80;

    public enum DiffKind { Changed, Added, Removed, Reordered, FormattingChanged }

    /// <summary>
    /// A step of a field path: a property, which for a list item also names the item by its position (from 1)
    /// or, in a list of links, by the record it links to.
    /// </summary>
    public sealed record PathStep(string Name, int? Position = null, string? Ref = null);

    /// <summary>
    /// One difference between two versions. <see cref="Old"/> and <see cref="New"/> are the values on each side,
    /// null where that side has none; for <see cref="DiffKind.Added"/> and <see cref="DiffKind.Removed"/> they
    /// hold the one list item added or removed.
    /// </summary>
    public sealed record FieldDiff(IReadOnlyList<PathStep> Path, DiffKind Kind, JsonElement? Old, JsonElement? New)
    {
        /// <summary>The property the values belong to, which tells what a handle among them links to.</summary>
        public string Field => Path.Count == 0 ? "" : Path[^1].Name;
    }

    /// <summary>Gramps bookkeeping that every save changes, and the record's own handle.</summary>
    private static readonly HashSet<string> SkippedFields = new(StringComparer.Ordinal) { "_class", "change", "handle" };

    /// <summary>Properties that hold handles, or lists of links by <c>ref</c>, and the collection they link to.</summary>
    private static readonly Dictionary<string, string> LinkCollections = new(StringComparer.Ordinal)
    {
        ["family_list"] = "families",
        ["parent_family_list"] = "families",
        ["father_handle"] = "people",
        ["mother_handle"] = "people",
        ["child_ref_list"] = "people",
        ["person_ref_list"] = "people",
        ["event_ref_list"] = "events",
        ["place"] = "places",
        ["placeref_list"] = "places",
        ["source_handle"] = "sources",
        ["citation_list"] = "citations",
        ["note_list"] = "notes",
        ["media_list"] = "media",
        ["reporef_list"] = "repositories",
        ["tag_list"] = "tags",
    };

    /// <summary>Gramps type classes and the <c>/api/types/default/{category}/map</c> that names their values.</summary>
    private static readonly Dictionary<string, string> TypeCategories = new(StringComparer.Ordinal)
    {
        ["EventType"] = "event_types",
        ["EventRoleType"] = "event_role_types",
        ["FamilyRelType"] = "family_relation_types",
        ["NameType"] = "name_types",
        ["NameOriginType"] = "name_origin_types",
        ["AttributeType"] = "attribute_types",
        ["SrcAttributeType"] = "source_attribute_types",
        ["ChildRefType"] = "child_reference_types",
        ["NoteType"] = "note_types",
        ["PlaceType"] = "place_types",
        ["RepositoryType"] = "repository_types",
        ["SourceMediaType"] = "source_media_types",
        ["UrlType"] = "url_types",
    };

    private static readonly string[] GenderLabels = ["Female", "Male", "Unknown", "Other"];

    /// <summary>Numbers whose 0 is the default, such as a name's sort_as and display_as (the tree's format).</summary>
    private static readonly HashSet<string> ZeroIsDefault = new(StringComparer.Ordinal) { "sort_as", "display_as" };

    private const string None = "(none)";

    /// <summary>
    /// The differences between two stored versions of a record, in the order of its fields. A side that is
    /// missing, null or an empty object, as Gramps Web sends for the side an add or a delete does not have, counts
    /// as no record: the other side's fields that hold a value are then listed as <see cref="DiffKind.Changed"/>
    /// from or to nothing. Lists of handles compare as sets, lists of links by the record they link to, and other
    /// lists by position.
    /// </summary>
    public static IReadOnlyList<FieldDiff> Compare(JsonElement? before, JsonElement? after)
    {
        var diffs = new List<FieldDiff>();
        Walk([], Present(before), Present(after), diffs);
        return diffs;
    }

    /// <summary>The type categories the shown values need, such as <c>event_role_types</c>.</summary>
    public static IEnumerable<string> TypeCategoriesOf(IEnumerable<FieldDiff> diffs) =>
        diffs.SelectMany(d => new[] { d.Old, d.New })
            .SelectMany(value => value is { } v ? TypeClasses(v) : [])
            .Select(cls => TypeCategories.GetValueOrDefault(cls))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    /// <summary>The records the diffs link to, by collection, so their labels can be read in one batch.</summary>
    public static IEnumerable<(string Collection, string Handle)> LinkedRecords(IEnumerable<FieldDiff> diffs)
    {
        foreach (var diff in diffs)
        {
            foreach (var step in diff.Path)
            {
                if (step.Ref is { } r && LinkCollections.TryGetValue(step.Name, out var stepCollection))
                    yield return (stepCollection, r);
            }

            if (!LinkCollections.TryGetValue(diff.Field, out var collection))
                continue;
            foreach (var value in new[] { diff.Old, diff.New })
            {
                if (value is not { } v)
                    continue;
                var handle = v.ValueKind == JsonValueKind.String ? v.GetString() : RefOf(v);
                if (!string.IsNullOrWhiteSpace(handle))
                    yield return (collection, handle.Trim());
            }
        }
    }

    /// <summary>
    /// "RECORD HISTORY (2 of 2 changes, newest first)", the record, and per change
    /// "1. 2026-11-17 10:00:00 UTC — Updated — by Jane Doe [transaction: 12]" with up to
    /// <see cref="MaxFieldLinesPerChange"/> lines for the fields it changed: <c>field: before → after</c> for an
    /// update, the values it was added with for an add, and the values it held for a delete.
    /// </summary>
    /// <param name="record">The record line, such as <c>Person I0004: Smith, John [handle: h]</c>.</param>
    /// <param name="diffs">The differences of each change, in the order of <paramref name="changes"/>.</param>
    /// <param name="labels">Labels of linked records by handle, from <see cref="LinkedObjectLabels"/>.</param>
    /// <param name="typeLabels">Type names by category, from <see cref="GrampsDefaultTypeLabels"/>.</param>
    public static string FormatHistory(
        string record,
        IReadOnlyList<GrampsObjectChange> changes,
        IReadOnlyList<IReadOnlyList<FieldDiff>> diffs,
        int totalCount,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, IReadOnlyList<string>?> typeLabels)
    {
        var names = new Names(labels, typeLabels);
        var total = Math.Max(totalCount, changes.Count);
        var sb = new StringBuilder();
        sb.AppendLine(changes.Count == 0
            ? "RECORD HISTORY"
            : $"RECORD HISTORY ({changes.Count} of {total} change{(total == 1 ? "" : "s")}, newest first)");
        sb.AppendLine(record);
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        if (changes.Count == 0)
        {
            sb.AppendLine("No changes of this record are in the history. Gramps Web keeps the changes made through it; " +
                          "edits made in Gramps desktop are not in it.");
            return sb.ToString();
        }

        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            var kind = KindOf(change);
            var parts = new List<string>();
            if (SystemFormatter.FormatUnixTime(change.Timestamp) is { } time)
                parts.Add(time);
            parts.Add(kind);
            if (SystemFormatter.FormatUser(change.Connection?.User) is { } user)
                parts.Add($"by {user}");
            var transaction = change.TransactionId is { } id ? $" [transaction: {id}]" : "";
            sb.AppendLine($"{i + 1}. {string.Join(" — ", parts)}{transaction}");

            var snapshot = change.TransType is 0 or 2;
            var lines = (i < diffs.Count ? diffs[i] : [])
                .Select(diff => Line(diff, snapshot, names))
                .OfType<string>()
                .ToList();
            foreach (var line in lines.Take(MaxFieldLinesPerChange))
                sb.AppendLine($"   {line}");
            if (lines.Count > MaxFieldLinesPerChange)
                sb.AppendLine($"   … (+{lines.Count - MaxFieldLinesPerChange} more fields)");
            else if (lines.Count == 0 && change.TransType == 1)
                sb.AppendLine("   (no field changed)");
        }

        if (total > changes.Count)
        {
            sb.AppendLine();
            sb.AppendLine($"(+{total - changes.Count} older changes; a larger limit, up to 100, shows more.)");
        }

        return sb.ToString();
    }

    private static string KindOf(GrampsTransactionChange change) => change.TransType switch
    {
        0 => "Added",
        1 => "Updated",
        2 => "Deleted",
        _ => "Changed"
    };

    private sealed record Names(
        IReadOnlyDictionary<string, string> Records,
        IReadOnlyDictionary<string, IReadOnlyList<string>?> Types);

    // ── Comparing ─────────────────────────────────────────────────────────────

    private static void Walk(List<PathStep> path, JsonElement? old, JsonElement? @new, List<FieldDiff> diffs)
    {
        if (old is null && @new is null)
            return;

        if (IsKind(old, @new, JsonValueKind.Object) && !IsAtomic(old) && !IsAtomic(@new))
        {
            foreach (var name in FieldNames(old, @new))
            {
                path.Add(new PathStep(name));
                Walk(path, Child(old, name), Child(@new, name), diffs);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }

        if (IsKind(old, @new, JsonValueKind.Array) && !IsScalarList(old) && !IsScalarList(@new))
        {
            WalkList(path, old, @new, diffs);
            return;
        }

        var field = path.Count == 0 ? "" : path[^1].Name;
        if (SameValue(old, @new) || (IsEmpty(old, field) && IsEmpty(@new, field)))
            return;
        var kind = IsClass(old, "StyledText") && IsClass(@new, "StyledText") && TextOf(old) == TextOf(@new)
            ? DiffKind.FormattingChanged
            : DiffKind.Changed;
        diffs.Add(new FieldDiff(path.ToArray(), kind, old, @new));
    }

    private static void WalkList(List<PathStep> path, JsonElement? old, JsonElement? @new, List<FieldDiff> diffs)
    {
        var before = Items(old);
        var after = Items(@new);
        if (before.Count == 0 && after.Count == 0)
            return;
        var all = before.Concat(after).ToList();
        var list = path[^1];

        if (all.All(item => item.ValueKind == JsonValueKind.String))
        {
            var oldHandles = before.Select(item => item.GetString()).ToList();
            var newHandles = after.Select(item => item.GetString()).ToList();
            var removed = before.Where(item => !newHandles.Contains(item.GetString())).ToList();
            var added = after.Where(item => !oldHandles.Contains(item.GetString())).ToList();
            foreach (var item in removed)
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Removed, item, null));
            foreach (var item in added)
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Added, null, item));
            if (removed.Count == 0 && added.Count == 0 && !oldHandles.SequenceEqual(newHandles))
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Reordered, old, @new));
            return;
        }

        if (all.All(item => RefOf(item) is not null))
        {
            // The n-th link to a record on one side pairs with the n-th link to it on the other.
            var unmatched = after.Select((item, index) => (Item: item, Index: index)).ToList();
            var pairs = new List<(JsonElement Old, JsonElement New)>();
            var removed = new List<JsonElement>();
            foreach (var item in before)
            {
                var match = unmatched.FindIndex(candidate => RefOf(candidate.Item) == RefOf(item));
                if (match < 0)
                {
                    removed.Add(item);
                    continue;
                }
                pairs.Add((item, unmatched[match].Item));
                unmatched.RemoveAt(match);
            }

            foreach (var item in removed)
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Removed, item, null));
            foreach (var (item, _) in unmatched)
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Added, null, item));
            if (removed.Count == 0 && unmatched.Count == 0
                && !before.Select(RefOf).SequenceEqual(after.Select(RefOf)))
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Reordered, old, @new));

            foreach (var (oldItem, newItem) in pairs)
            {
                path[^1] = list with { Ref = RefOf(oldItem) };
                Walk(path, oldItem, newItem, diffs);
            }
            path[^1] = list;
            return;
        }

        for (var i = 0; i < Math.Max(before.Count, after.Count); i++)
        {
            if (i < before.Count && i < after.Count)
            {
                path[^1] = list with { Position = i + 1 };
                Walk(path, before[i], after[i], diffs);
                path[^1] = list;
            }
            else if (i < before.Count)
            {
                if (!IsEmpty(before[i], list.Name))
                    diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Removed, before[i], null));
            }
            else if (!IsEmpty(after[i], list.Name))
            {
                diffs.Add(new FieldDiff(path.ToArray(), DiffKind.Added, null, after[i]));
            }
        }
    }

    private static JsonElement? Present(JsonElement? value) =>
        value is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } v
        && !(v.ValueKind == JsonValueKind.Object && !v.EnumerateObject().Any())
            ? v
            : null;

    private static JsonElement? Child(JsonElement? value, string name) =>
        value is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty(name, out var child) ? Present(child) : null;

    /// <summary>The properties of either side, in stored order: the old side's first, then those only the new side has.</summary>
    private static IEnumerable<string> FieldNames(JsonElement? old, JsonElement? @new) =>
        new[] { old, @new }
            .Where(v => v is { ValueKind: JsonValueKind.Object })
            .SelectMany(v => v!.Value.EnumerateObject().Select(p => p.Name))
            .Distinct(StringComparer.Ordinal)
            .Where(name => !SkippedFields.Contains(name));

    private static bool IsKind(JsonElement? old, JsonElement? @new, JsonValueKind kind) =>
        (old ?? @new)?.ValueKind == kind && (@new ?? old)?.ValueKind == kind;

    private static List<JsonElement> Items(JsonElement? list) =>
        list is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray().ToList() : [];

    /// <summary>A list of numbers or booleans, such as a media region, compared and shown as one value.</summary>
    private static bool IsScalarList(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Array } v && v.GetArrayLength() > 0
        && v.EnumerateArray().All(item => item.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False);

    private static string? ClassOf(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Object } v && v.TryGetProperty("_class", out var cls)
                                                       && cls.ValueKind == JsonValueKind.String
            ? cls.GetString()
            : null;

    private static bool IsClass(JsonElement? value, string className) => ClassOf(value) == className;

    private static bool IsTypeObject(JsonElement? value) =>
        ClassOf(value) is { } cls && cls.EndsWith("Type", StringComparison.Ordinal)
                                  && value!.Value.TryGetProperty("value", out _);

    /// <summary>Dates, styled texts and types, which are compared and shown as one value.</summary>
    private static bool IsAtomic(JsonElement? value) =>
        ClassOf(value) is "Date" or "StyledText" || IsTypeObject(value);

    private static string? RefOf(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty("ref", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString()
            : null;

    private static string? TextOf(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.String } v => v.GetString(),
        { ValueKind: JsonValueKind.Object } v when IsClass(v, "StyledText")
            && v.TryGetProperty("string", out var s) && s.ValueKind == JsonValueKind.String => s.GetString(),
        _ => null
    };

    private static bool SameValue(JsonElement? a, JsonElement? b) =>
        a is { } x && b is { } y ? JsonElement.DeepEquals(x, y) : a is null && b is null;

    /// <summary>
    /// No value worth a line: nothing, an empty text, list, date or object, <c>false</c>, or the <c>-1</c> of an unset
    /// birth or death reference.
    /// </summary>
    private static bool IsEmpty(JsonElement? value, string field) => value is not { } v || v.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => true,
        JsonValueKind.String => v.GetString()!.Length == 0,
        JsonValueKind.Array => v.EnumerateArray().All(item => IsEmpty(item, field)),
        JsonValueKind.Number => v.TryGetInt32(out var number)
                                && ((field.EndsWith("_ref_index", StringComparison.Ordinal) && number < 0)
                                    || (ZeroIsDefault.Contains(field) && number == 0)),
        JsonValueKind.Object => ClassOf(v) switch
        {
            "Date" => IsEmptyDate(v),
            "StyledText" => string.IsNullOrEmpty(TextOf(v)),
            _ when IsTypeObject(v) => false,
            _ => v.EnumerateObject().All(p => SkippedFields.Contains(p.Name) || IsEmpty(p.Value, p.Name))
        },
        _ => false
    };

    private static bool IsEmptyDate(JsonElement date) =>
        (!date.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String || text.GetString()!.Length == 0)
        && (!date.TryGetProperty("dateval", out var dateval) || dateval.ValueKind != JsonValueKind.Array
            || dateval.EnumerateArray().All(part => part.ValueKind switch
            {
                JsonValueKind.Number => part.TryGetInt32(out var n) && n == 0,
                JsonValueKind.False => true,
                _ => false
            }));

    private static IEnumerable<string> TypeClasses(JsonElement value)
    {
        if (IsTypeObject(value))
        {
            yield return ClassOf(value)!;
            yield break;
        }

        var children = value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject().Select(p => p.Value),
            JsonValueKind.Array => value.EnumerateArray(),
            _ => []
        };
        foreach (var child in children)
        foreach (var cls in TypeClasses(child))
            yield return cls;
    }

    // ── Showing ───────────────────────────────────────────────────────────────

    /// <summary>The line of one difference; null for a field an added or deleted record held no value in.</summary>
    private static string? Line(FieldDiff diff, bool snapshot, Names names)
    {
        var path = PathText(diff.Path, names);
        var field = diff.Field;
        if (snapshot && Value(diff.New ?? diff.Old, field, names) == None)
            return null;
        return diff.Kind switch
        {
            DiffKind.Reordered => $"{path}: reordered",
            DiffKind.FormattingChanged => $"{path}: text formatting changed",
            DiffKind.Added when snapshot => $"{path}: {Value(diff.New, field, names)}",
            DiffKind.Removed when snapshot => $"{path}: {Value(diff.Old, field, names)}",
            DiffKind.Added => $"{path}: added {Value(diff.New, field, names)}",
            DiffKind.Removed => $"{path}: removed {Value(diff.Old, field, names)}",
            _ when snapshot => $"{path}: {Value(diff.New ?? diff.Old, field, names)}",
            _ when TextOf(diff.Old) is { } before && TextOf(diff.New) is { } after
                   && Math.Max(before.Length, after.Length) > MaxValueChars => $"{path}: {TextChange(before, after)}",
            _ => $"{path}: {Value(diff.Old, field, names)} → {Value(diff.New, field, names)}"
        };
    }

    /// <summary><c>primary_name.surname_list[1].surname</c>, with a link item named by the record it links to.</summary>
    private static string PathText(IReadOnlyList<PathStep> path, Names names) =>
        string.Join(".", path.Select(step => step switch
        {
            { Position: { } position } => $"{step.Name}[{position}]",
            { Ref: { } handle } => $"{step.Name}[{(names.Records.TryGetValue(handle, out var label) ? Cut(label, 40) : handle)}]",
            _ => step.Name
        }));

    private static string Value(JsonElement? value, string field, Names names)
    {
        if (value is not { } v || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return None;

        switch (v.ValueKind)
        {
            case JsonValueKind.String when LinkCollections.ContainsKey(field):
                return v.GetString() is { Length: > 0 } handle ? Link(handle, names) : None;
            case JsonValueKind.String:
                return Quote(v.GetString()!);
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Number:
                return NumberText(v, field);
            case JsonValueKind.Array when IsScalarList(v):
                return $"[{string.Join(", ", v.EnumerateArray().Select(item => Value(item, field, names)))}]";
            case JsonValueKind.Array:
                var items = v.EnumerateArray().Where(item => !IsEmpty(item, field)).ToList();
                return items.Count == 0 ? None : Cut(string.Join("; ", items.Select(item => Value(item, field, names))), MaxValueChars * 2);
        }

        if (ClassOf(v) == "Date")
            return IsEmptyDate(v) ? "(no date)" : DateText(v);
        if (ClassOf(v) == "StyledText")
            return Quote(TextOf(v) ?? "");
        if (IsTypeObject(v))
            return TypeText(v, names);
        if (RefOf(v) is { } linked && LinkCollections.ContainsKey(field))
        {
            var details = Details(v, names, except: "ref");
            return details.Length == 0 ? Link(linked, names) : $"{Link(linked, names)} ({details})";
        }

        var fields = Details(v, names);
        return fields.Length == 0 ? None : $"{{{fields}}}";
    }

    /// <summary><c>role: Primary, private: true</c>: the fields of an object that hold a value.</summary>
    private static string Details(JsonElement value, Names names, string? except = null) =>
        Cut(string.Join(", ", value.EnumerateObject()
            .Where(p => !SkippedFields.Contains(p.Name) && p.Name != except && !IsEmpty(p.Value, p.Name))
            .Select(p => (p.Name, Text: Value(p.Value, p.Name, names)))
            .Where(p => p.Text != None)
            .Select(p => $"{p.Name}: {p.Text}")), MaxValueChars * 2);

    private static string Link(string handle, Names names) =>
        names.Records.TryGetValue(handle, out var label) ? $"{label} [handle: {handle}]" : $"[handle: {handle}]";

    private static string NumberText(JsonElement value, string field)
    {
        if (!value.TryGetInt32(out var number))
            return value.GetRawText();
        return field switch
        {
            "gender" when number >= 0 && number < GenderLabels.Length => GenderLabels[number],
            "confidence" when number >= 0 && number < CitationFormatter.ConfidenceLabels.Length =>
                CitationFormatter.ConfidenceLabels[number],
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string DateText(JsonElement date)
    {
        try
        {
            return date.Deserialize<GrampsDate>(GrampsJson.Options) is { } parsed
                ? GrampsValueFormatter.FormatDate(parsed)
                : "(no date)";
        }
        catch (JsonException)
        {
            return Cut(date.GetRawText(), MaxValueChars);
        }
    }

    /// <summary>A custom type by its name; a built-in one by the name of its value.</summary>
    private static string TypeText(JsonElement type, Names names)
    {
        if (type.TryGetProperty("string", out var custom) && custom.ValueKind == JsonValueKind.String
            && custom.GetString()?.Trim() is { Length: > 0 } name)
            return name;
        if (!type.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
            return None;
        var labels = TypeCategories.TryGetValue(ClassOf(type)!, out var category)
            ? names.Types.GetValueOrDefault(category)
            : null;
        // A built-in value without a name, such as the "no origin" of a surname.
        if (labels is not null && value.TryGetInt32(out var number) && number > 0 && number < labels.Count
            && labels[number].Length == 0)
            return None;
        return GrampsDefaultTypeLabels.ResolveStored(value.GetRawText(), labels);
    }

    /// <summary>
    /// A long text that changed: its lengths and the part that differs, with some text around it, such as
    /// <c>1520 → 1534 characters, from character 341: "…born in 1890 at…" → "…born in 1891 at…"</c>.
    /// </summary>
    private static string TextChange(string before, string after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix])
            prefix++;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
               && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            suffix++;

        return $"{before.Length} → {after.Length} characters, from character {prefix + 1}: " +
               $"{Excerpt(before, prefix, before.Length - suffix)} → {Excerpt(after, prefix, after.Length - suffix)}";
    }

    private static string Excerpt(string text, int start, int end)
    {
        const int context = 20, middle = 40;
        var from = Math.Max(0, start - context);
        var to = Math.Min(text.Length, end + context);
        var changed = text[start..end];
        if (changed.Length > middle)
            changed = changed[..(middle - 1)] + "…";
        var sb = new StringBuilder("\"");
        if (from > 0)
            sb.Append('…');
        sb.Append(text[from..start]).Append(changed).Append(text[end..to]);
        if (to < text.Length)
            sb.Append('…');
        return OneLine(sb.Append('"').ToString());
    }

    private static string Quote(string text) =>
        text.Length == 0 ? "(empty)" : $"\"{OneLine(Cut(text, MaxValueChars))}\"";

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string OneLine(string text) => text.Replace("\r\n", "↵").Replace('\n', '↵').Replace('\r', '↵');
}
