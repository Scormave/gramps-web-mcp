using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Builds a place timeline from <c>GET /api/places/{handle}?backlinks=true</c> event handles
/// (the API spec has no <c>/places/{handle}/timeline</c> route).
/// </summary>
internal static class PlaceTimelineFallback
{
    public static async Task<GrampsTimelineEntry[]> CollectAsync(
        GrampsApiClient client,
        string placeHandle,
        GrampsPlace place)
    {
        var raw = await client.GetJsonOrNullIfNotFoundAsync($"/api/places/{placeHandle}?backlinks=true");
        if (raw is null || raw.Value.ValueKind != JsonValueKind.Object)
            return [];

        var root = raw.Value;
        if (!root.TryGetProperty("backlinks", out var backlinks) || backlinks.ValueKind != JsonValueKind.Object)
            return [];

        var eventHandles = CollectEventHandles(backlinks);
        if (eventHandles.Count == 0)
            return [];

        var list = new List<GrampsTimelineEntry>();
        foreach (var eh in eventHandles)
        {
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{eh}");
            if (evt is null)
                continue;
            if (!string.Equals(evt.Place, placeHandle, StringComparison.Ordinal))
                continue;
            list.Add(ToTimelineEntry(evt, place));
        }

        return list.ToArray();
    }

    private static HashSet<string> CollectEventHandles(JsonElement backlinks)
    {
        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "event", "events" })
        {
            if (!backlinks.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        handles.Add(s);
                }
            }
        }

        return handles;
    }

    private static GrampsTimelineEntry ToTimelineEntry(GrampsEvent evt, GrampsPlace place)
    {
        var dateDisplay = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : "";
        return new GrampsTimelineEntry
        {
            Handle = evt.Handle,
            GrampsId = evt.GrampsId,
            Type = evt.Type,
            Date = dateDisplay,
            Description = evt.Description,
            Label = !string.IsNullOrWhiteSpace(evt.Description) ? evt.Description.Trim() : evt.Type,
            Place = new GrampsTimelinePlaceProfile
            {
                Handle = place.Handle,
                Name = place.Name,
                DisplayName = place.Name
            }
        };
    }
}
