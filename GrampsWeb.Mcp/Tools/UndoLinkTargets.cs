using System.Text.Json;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The records a stored Gramps object links to, read from its raw JSON as the transaction history keeps it
/// (<c>old_data</c>). Undo writes such a version back unchanged, so each link must still have a target.
/// </summary>
internal static class UndoLinkTargets
{
    // Keys holding one handle, at any depth: an event's or LDS ordinance's place, a family's partners,
    // a citation's source, an LDS ordinance's family.
    private static readonly Dictionary<string, string> HandleKeys = new(StringComparer.Ordinal)
    {
        ["place"] = "places",
        ["father_handle"] = "people",
        ["mother_handle"] = "people",
        ["source_handle"] = "sources",
        ["famc"] = "families"
    };

    // Keys holding a list of handles.
    private static readonly Dictionary<string, string> HandleListKeys = new(StringComparer.Ordinal)
    {
        ["note_list"] = "notes",
        ["citation_list"] = "citations",
        ["tag_list"] = "tags",
        ["family_list"] = "families",
        ["parent_family_list"] = "families"
    };

    // Keys holding a list of references, each linking its target under "ref".
    private static readonly Dictionary<string, string> RefListKeys = new(StringComparer.Ordinal)
    {
        ["event_ref_list"] = "events",
        ["child_ref_list"] = "people",
        ["person_ref_list"] = "people",
        ["media_list"] = "media",
        ["reporef_list"] = "repositories",
        ["placeref_list"] = "places"
    };

    /// <summary>Distinct (collection, handle) pairs the object links to, references' own notes and citations included.</summary>
    public static IReadOnlyCollection<(string Collection, string Handle)> Collect(JsonElement data)
    {
        var targets = new HashSet<(string, string)>();
        Walk(data, targets);
        return targets;
    }

    private static void Walk(JsonElement element, HashSet<(string, string)> targets)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, targets);
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value;
            if (HandleKeys.TryGetValue(property.Name, out var collection) && value.ValueKind == JsonValueKind.String)
                Add(targets, collection, value.GetString());
            else if (HandleListKeys.TryGetValue(property.Name, out collection) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        Add(targets, collection, item.GetString());
                }
            }
            else if (RefListKeys.TryGetValue(property.Name, out collection) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("ref", out var target)
                        && target.ValueKind == JsonValueKind.String)
                        Add(targets, collection, target.GetString());
                }
            }

            Walk(value, targets);
        }
    }

    private static void Add(HashSet<(string, string)> targets, string collection, string? handle)
    {
        if (!string.IsNullOrWhiteSpace(handle))
            targets.Add((collection, handle.Trim()));
    }
}
