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
    /// <summary>Participant names listed per row before the rest are counted.</summary>
    internal const int MaxParticipantsShown = 5;

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
            Participants = FormatParticipants(evt.Profile?.Participants)
        };
    }

    /// <summary>
    /// "Name (I0001), Name (I0002) [Witness]"; families show as "Father and Mother (F0001)".
    /// Primary and Family roles are implied and left out.
    /// </summary>
    internal static string? FormatParticipants(GrampsEventParticipants? participants)
    {
        if (participants is null)
            return null;

        var names = new List<string>();
        foreach (var p in participants.People ?? [])
        {
            if (p.Person is not { } person || string.IsNullOrWhiteSpace(person.NameDisplay))
                continue;
            names.Add(WithRole(WithId(person.NameDisplay.Trim(), person.GrampsId), p.Role));
        }

        foreach (var f in participants.Families ?? [])
        {
            if (f.Family is not { } family)
                continue;
            var parents = new[] { family.Father?.NameDisplay, family.Mother?.NameDisplay }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!.Trim())
                .ToArray();
            var label = parents.Length > 0 ? string.Join(" and ", parents) : "Family";
            // "Family" is the usual role of a family event.
            var role = string.Equals(f.Role?.Trim(), "Family", StringComparison.OrdinalIgnoreCase) ? null : f.Role;
            names.Add(WithRole(WithId(label, family.GrampsId), role));
        }

        if (names.Count == 0)
            return null;
        if (names.Count <= MaxParticipantsShown)
            return string.Join(", ", names);
        return string.Join(", ", names.Take(MaxParticipantsShown)) + $", … (+{names.Count - MaxParticipantsShown} more)";
    }

    private static string WithId(string label, string? grampsId) =>
        string.IsNullOrWhiteSpace(grampsId) ? label : $"{label} ({grampsId.Trim()})";

    private static string WithRole(string label, string? role) =>
        string.IsNullOrWhiteSpace(role) || role.Trim().Equals("Primary", StringComparison.OrdinalIgnoreCase)
            ? label
            : $"{label} [{role.Trim()}]";
}
