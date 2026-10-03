using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Client-side filters for place timelines (no server <c>/places/…/timeline</c> route).
/// Event classes match Gramps Web timeline <c>event_classes</c> keywords and default type lists from the API spec.
/// </summary>
internal static class PlaceTimelineFilters
{
    /// <summary>Keyword → default English event type strings (Gramps defaults; localized trees may differ).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> EventTypesByClass =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["vital"] =
            [
                "Birth", "Adoption", "Baptism", "Death", "Burial", "Cremation"
            ],
            ["family"] =
            [
                "Engagement", "Marriage", "Marriage Settlement", "Marriage License", "Marriage Contract",
                "Marriage Banns", "Divorce Filing", "Divorce", "Annulment", "Alternate Marriage"
            ],
            ["religious"] =
            [
                "Christening", "Adult Christening", "Confirmation", "First Communion", "Blessing",
                "Bar Mitzvah", "Bas Mitzvah", "Religion"
            ],
            ["vocational"] = ["Occupation", "Retirement", "Elected", "Military Service", "Ordination"],
            ["academic"] = ["Education", "Degree", "Graduation"],
            ["travel"] = ["Emigration", "Immigration", "Naturalization"],
            ["legal"] = ["Probate", "Will"],
            ["residence"] = ["Residence", "Census", "Property"],
            ["other"] =
            [
                "Cause of Death", "Medical Information", "Title of Nobility", "Number of Marriages"
            ]
        };

    private static readonly HashSet<string> AllDefaultTypeNames = BuildAllDefaultTypeNames();

    private static HashSet<string> BuildAllDefaultTypeNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var types in EventTypesByClass.Values)
        {
            foreach (var t in types)
                set.Add(t);
        }

        return set;
    }

    internal static bool Passes(GrampsEvent evt, in PlaceTimelineCollectOptions options, TimelineSdnRange? dateRange)
    {
        if (!MatchesEventClasses(evt.Type, options.EventClasses))
            return false;

        var sortKey = GrampsDateSortVal.TryGetTimelineSortKey(evt.Date);

        if (!options.IncludeUndated)
        {
            if (sortKey is null or 0)
                return false;
        }

        if (dateRange is { } r)
        {
            if (sortKey is null or 0)
                return false;
            if (!r.Contains(sortKey.Value))
                return false;
        }

        return true;
    }

    internal static bool MatchesEventClasses(string? eventType, string[]? requestedClasses)
    {
        if (requestedClasses == null || requestedClasses.Length == 0)
            return true;

        var type = string.IsNullOrWhiteSpace(eventType) ? "" : eventType.Trim();
        if (type.Length == 0)
            return false;

        var wanted = new HashSet<string>(
            requestedClasses.Select(c => c.Trim()).Where(c => c.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var wantsCustom = wanted.Remove("custom");

        foreach (var cls in wanted)
        {
            if (!EventTypesByClass.TryGetValue(cls, out var types))
                continue;
            foreach (var t in types)
            {
                if (string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (wantsCustom && !AllDefaultTypeNames.Contains(type))
            return true;

        return false;
    }
}

internal readonly record struct PlaceTimelineCollectOptions(
    string[]? EventClasses = null,
    bool IncludeUndated = true);

internal readonly record struct PlaceTimelineCollectOutcome(
    GrampsTimelineEntry[] Entries,
    int MatchedPlaceCount);
