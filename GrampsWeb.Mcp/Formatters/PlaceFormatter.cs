using System.Collections;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats place API responses and simple hierarchy display.
/// </summary>
public static class PlaceFormatter
{
    /// <summary>
    /// Place name with type when available (reserved for future parent traversal).
    /// </summary>
    public static Task<string> FormatPlaceHierarchy(GrampsPlace place, GrampsApiClient? client, int maxLevels = 6)
    {
        if (place == null)
            return Task.FromResult("Unknown place");

        var result = place.Name ?? "Unknown";
        if (!string.IsNullOrEmpty(place.Type))
            result += $" ({place.Type})";

        return Task.FromResult(result);
    }

    public static async Task<string> FormatPlaceFull(GrampsPlace place, GrampsApiClient client)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PLACE: {place.Name} [handle: {place.Handle}] (gramps_id: {place.GrampsId})");
        sb.AppendLine(new string('=', 60));

        if (!string.IsNullOrEmpty(place.Type))
            sb.AppendLine($"Type: {place.Type}");

        var hierarchy = await BuildPlaceHierarchy(place, client);
        if (!string.IsNullOrEmpty(hierarchy))
            sb.AppendLine($"Hierarchy: {hierarchy}");

        if (!string.IsNullOrEmpty(place.Latitude) || !string.IsNullOrEmpty(place.Longitude))
        {
            sb.AppendLine($"Coordinates: {place.Latitude ?? "—"}, {place.Longitude ?? "—"}");
        }

        if (place.CitationList?.Length > 0)
            sb.AppendLine($"Citations: {place.CitationList.Length}");
        if (place.NoteList?.Length > 0)
            sb.AppendLine($"Notes:     {place.NoteList.Length}");
        if (place.MediaList?.Length > 0)
            sb.AppendLine($"Media:     {place.MediaList.Length}");
        if (place.TagList?.Length > 0)
            sb.AppendLine($"Tags:      {string.Join(", ", place.TagList)}");

        return sb.ToString();
    }

    private static async Task<string> BuildPlaceHierarchy(GrampsPlace place, GrampsApiClient client)
    {
        var parents = new List<string> { place.Name ?? "Unknown" };

        if (place.PlaceRefList is not null && place.PlaceRefList.Length > 0)
        {
            foreach (var pref in place.PlaceRefList)
            {
                try
                {
                    if (pref is string refStr && !string.IsNullOrEmpty(refStr))
                    {
                        var parent = await client.GetAsync<GrampsPlace>($"/api/places/{refStr}");
                        if (parent?.Name != null)
                            parents.Add(parent.Name);
                    }
                    else if (pref is IDictionary dict && dict["ref"] is string refHandle)
                    {
                        var parent = await client.GetAsync<GrampsPlace>($"/api/places/{refHandle}");
                        if (parent?.Name != null)
                            parents.Add(parent.Name);
                    }
                }
                catch { }
            }
        }

        parents.Reverse();
        return string.Join(", ", parents);
    }
}
