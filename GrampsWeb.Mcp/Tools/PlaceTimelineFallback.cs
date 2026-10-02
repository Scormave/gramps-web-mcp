using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Builds a place timeline from <c>GET /api/places/{handle}?backlinks=true</c> event handles
/// (the API spec has no <c>/places/{handle}/timeline</c> route). Rows leave the place out, since
/// every event is at it.
/// </summary>
internal static class PlaceTimelineFallback
{
    public static async Task<PlaceTimelineCollectOutcome> CollectAsync(
        GrampsApiClient client,
        string placeHandle,
        string[]? eventClasses,
        string? datesNormalized,
        bool includeUndated)
    {
        var raw = await client.GetJsonOrNullIfNotFoundAsync(
            $"/api/places/{Uri.EscapeDataString(placeHandle)}?backlinks=true");
        if (raw is null || raw.Value.ValueKind != JsonValueKind.Object)
            return new PlaceTimelineCollectOutcome([], 0);

        var root = raw.Value;
        if (!root.TryGetProperty("backlinks", out var backlinks) || backlinks.ValueKind != JsonValueKind.Object)
            return new PlaceTimelineCollectOutcome([], 0);

        var eventHandles = CollectEventHandles(backlinks);
        if (eventHandles.Count == 0)
            return new PlaceTimelineCollectOutcome([], 0);

        var range = PlaceTimelineFilters.TryParseDateRange(datesNormalized);
        var options = new PlaceTimelineCollectOptions(eventClasses, includeUndated);

        // The participants profile names the people and families in the same response.
        var events = await client.GetByHandlesAsync<GrampsEvent>(
            "events", eventHandles, e => e.Handle, query: "profile=participants");

        var matched = new List<GrampsEvent>();
        var matchedPlace = 0;
        foreach (var eh in eventHandles)
        {
            if (!events.TryGetValue(eh, out var evt))
                continue;
            if (!string.Equals(evt.Place, placeHandle, StringComparison.Ordinal))
                continue;
            matchedPlace++;
            if (!PlaceTimelineFilters.Passes(evt, options, range))
                continue;
            matched.Add(evt);
        }

        // Backlinks come in no particular order; sortval orders events within a year, as the timeline routes do.
        var entries = matched
            .OrderBy(e => e.Date?.SortVal ?? 0)
            .Select(ToTimelineEntry)
            .ToArray();
        return new PlaceTimelineCollectOutcome(entries, matchedPlace);
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

    private static GrampsTimelineEntry ToTimelineEntry(GrampsEvent evt)
    {
        var dateDisplay = evt.Date != null ? GrampsValueFormatter.FormatDate(evt.Date) : "";
        return new GrampsTimelineEntry
        {
            Handle = evt.Handle,
            GrampsId = evt.GrampsId,
            Type = evt.Type,
            Date = dateDisplay,
            Description = evt.Description,
            Participants = EventParticipantsFormatter.FormatParticipants(evt.Profile?.Participants)
        };
    }
}
