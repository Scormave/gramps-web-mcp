using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Fetches and parses Gramps Web <c>GET /api/{segment}/{handle}?backlinks=true</c> payloads without mutating Swagger DTOs.
/// </summary>
public static class BacklinkCollector
{
    private static readonly string[] DisplayOrder =
    [
        "people", "families", "events", "places", "sources", "citations", "media", "notes", "repositories"
    ];

    private static readonly Dictionary<string, string> JsonKeyToCanonical =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = "people",
            ["people"] = "people",
            ["family"] = "families",
            ["families"] = "families",
            ["event"] = "events",
            ["events"] = "events",
            ["place"] = "places",
            ["places"] = "places",
            ["source"] = "sources",
            ["sources"] = "sources",
            ["citation"] = "citations",
            ["citations"] = "citations",
            ["media"] = "media",
            ["note"] = "notes",
            ["notes"] = "notes",
            ["repository"] = "repositories",
            ["repositories"] = "repositories",
        };

    public static async Task<IReadOnlyList<BacklinkGroup>> CollectAsync(
        GrampsApiClient client,
        string apiPathSegment,
        string handle)
    {
        var raw = await client.GetJsonOrNullIfNotFoundAsync(
            $"/api/{apiPathSegment}/{Uri.EscapeDataString(handle)}?backlinks=true");
        return ParseGroupsFromResponseRoot(raw);
    }

    /// <summary>Parses <c>backlinks</c> from a full object JSON root (for tests and reuse).</summary>
    internal static IReadOnlyList<BacklinkGroup> ParseGroupsFromResponseRoot(JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Object } obj)
            return [];

        if (!obj.TryGetProperty("backlinks", out var backlinks) || backlinks.ValueKind != JsonValueKind.Object)
            return [];

        var buckets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var prop in backlinks.EnumerateObject())
        {
            if (!JsonKeyToCanonical.TryGetValue(prop.Name, out var canonical))
                continue;
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;

            if (!buckets.TryGetValue(canonical, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                buckets[canonical] = set;
            }

            foreach (var el in prop.Value.EnumerateArray())
            {
                var h = HandleElementReader.ReadHandleFromElement(el).Trim();
                if (h.Length > 0)
                    set.Add(h);
            }
        }

        var orderedKeys = new List<string>();
        foreach (var k in DisplayOrder)
        {
            if (buckets.TryGetValue(k, out var set) && set.Count > 0)
                orderedKeys.Add(k);
        }

        foreach (var k in buckets.Keys.OrderBy(static x => x, StringComparer.Ordinal))
        {
            if (!buckets.TryGetValue(k, out var bucket) || bucket.Count == 0 || orderedKeys.Contains(k))
                continue;
            orderedKeys.Add(k);
        }

        var groups = new List<BacklinkGroup>(orderedKeys.Count);
        foreach (var k in orderedKeys)
        {
            if (!buckets.TryGetValue(k, out var set) || set.Count == 0)
                continue;
            var handles = set.OrderBy(static x => x, StringComparer.Ordinal).ToList();
            groups.Add(new BacklinkGroup(k, k, handles));
        }

        return groups;
    }
}
