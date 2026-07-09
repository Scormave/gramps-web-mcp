using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats place API responses and simple hierarchy display.
/// </summary>
public static class PlaceFormatter
{
    private sealed record HierarchyLevel(
        string Name,
        string? Handle,
        string TypeLabel,
        GrampsDate? EnclosureDate);

    /// <summary>
    /// Place name with type when available (reserved for future parent traversal).
    /// </summary>
    public static async Task<string> FormatPlaceHierarchy(GrampsPlace place, GrampsApiClient? client, int maxLevels = 6)
    {
        if (place == null)
            return "Unknown place";

        var result = place.Name ?? "Unknown";
        if (!string.IsNullOrEmpty(place.Type))
        {
            var typeLabel = client != null
                ? await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, place.Type).ConfigureAwait(false)
                : place.Type.Trim();
            result += $" ({typeLabel})";
        }

        return result;
    }

    public static async Task<string> FormatPlaceFull(GrampsPlace place, GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PLACE: {place.Name} [handle: {place.Handle}] (gramps_id: {place.GrampsId})");
        sb.AppendLine(new string('=', 60));

        var typeLabel = await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, place.Type);
        sb.AppendLine($"Type: {typeLabel}");

        var hierarchy = await BuildPlaceHierarchy(place, client);
        if (!string.IsNullOrEmpty(hierarchy))
            sb.AppendLine($"Hierarchy: {hierarchy}");

        AppendEnclosedBySection(sb, place);

        if (!string.IsNullOrEmpty(place.Latitude) || !string.IsNullOrEmpty(place.Longitude))
        {
            sb.AppendLine($"Coordinates: {place.Latitude ?? "—"}, {place.Longitude ?? "—"}");
        }

        AppendAlternateNamesSection(sb, place);

        HandleListFormatter.AppendHandleBulletSection(sb, "Citations", place.CitationList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Notes", place.NoteList);
        HandleListFormatter.AppendHandleBulletSection(sb, "Media", GrampsMediaRef.ToHandleStrings(place.MediaList));
        HandleListFormatter.AppendHandleBulletSection(sb, "Tags", place.TagList);

        return sb.ToString();
    }

    private static async Task<string> BuildPlaceHierarchy(GrampsPlace place, GrampsApiClient client, int maxLevels = 6)
    {
        var levels = await CollectHierarchyLevels(place, client, maxLevels).ConfigureAwait(false);
        if (levels.Count == 0)
            return string.Empty;

        return string.Join(", ", levels.Select(FormatHierarchyLevel));
    }

    private static async Task<List<HierarchyLevel>> CollectHierarchyLevels(
        GrampsPlace place,
        GrampsApiClient client,
        int maxLevels)
    {
        var levels = new List<HierarchyLevel>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = place;
        GrampsDate? enclosureDate = null;

        for (var depth = 0; depth < maxLevels && current != null; depth++)
        {
            if (!string.IsNullOrEmpty(current.Handle) && !visited.Add(current.Handle))
                break;

            var typeLabel = await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, current.Type)
                .ConfigureAwait(false);
            levels.Add(new HierarchyLevel(
                current.Name ?? "Unknown",
                current.Handle,
                typeLabel,
                enclosureDate));

            var parentRef = current.PlaceRefList?
                .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Ref));
            if (parentRef?.Ref == null)
                break;

            enclosureDate = parentRef.Date;
            try
            {
                current = await client.GetAsync<GrampsPlace>(
                    $"/api/places/{Uri.EscapeDataString(parentRef.Ref)}").ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }

        return levels;
    }

    private static string FormatHierarchyLevel(HierarchyLevel level)
    {
        var parts = new List<string> { level.Name };
        if (!string.IsNullOrEmpty(level.Handle))
            parts.Add($"[{level.Handle}]");
        if (!string.IsNullOrWhiteSpace(level.TypeLabel))
            parts.Add($"({level.TypeLabel})");
        if (level.EnclosureDate != null)
            parts.Add($"[{GrampsValueFormatter.FormatDate(level.EnclosureDate)}]");

        return string.Join(" ", parts);
    }

    private static void AppendEnclosedBySection(StringBuilder sb, GrampsPlace place)
    {
        if (place.PlaceRefList is not { Length: > 0 })
            return;

        sb.AppendLine("Enclosed by:");
        foreach (var pref in place.PlaceRefList)
        {
            if (string.IsNullOrWhiteSpace(pref.Ref))
                continue;

            var line = $"  - {pref.Ref}";
            if (pref.Date != null)
                line += $" [{GrampsValueFormatter.FormatDate(pref.Date)}]";
            sb.AppendLine(line);
        }
    }

    private static void AppendAlternateNamesSection(StringBuilder sb, GrampsPlace place)
    {
        if (place.AlternateNames is not { Length: > 0 })
            return;

        sb.AppendLine($"Alternate names ({place.AlternateNames.Length}):");
        foreach (var alt in place.AlternateNames)
        {
            var value = alt.Value ?? "—";
            var lang = string.IsNullOrWhiteSpace(alt.Lang) ? null : alt.Lang;
            var date = alt.Date != null ? GrampsValueFormatter.FormatDate(alt.Date) : null;

            var line = $"  - {value}";
            if (lang != null)
                line += $" ({lang})";
            if (date != null)
                line += $" [{date}]";
            sb.AppendLine(line);
        }
    }
}
