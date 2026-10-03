using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool for chronological timelines of person, family, and place records.
/// </summary>
[McpServerToolType]
public static class TimelineTools
{
    [McpServerTool(Title = "Get Timeline", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: chronological timeline for one person, family, or place. " +
        "objectType must be person, family, or place. Events can be filtered by category and date range. " +
        "relatives and relativeEvents are supported only for person timelines. Without dates, a person timeline lists relatives' events " +
        "from their whole lives, also before the person's birth and after their death; pass dates to narrow it. " +
        "Place timelines are computed from direct event backlinks; child places are not included.")]
    public static async Task<string> GetTimeline(
        [Description("Timeline owner type: person | family | place.")]
        string objectType,
        [Description("Person, family, or place handle or Gramps ID.")]
        string identifier,
        [Description("Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom.")]
        string[]? events = null,
        [Description("For person only: include events of father, mother, brother, sister, wife, husband, son, or daughter.")]
        string[]? relatives = null,
        [Description("For person only: event categories for the listed relatives.")]
        string[]? relativeEvents = null,
        [Description(
            "Only events in these dates, written like any date: a year (1850), month (1850-03), day, or range " +
            "(1850-1860, between 1850-03 and 1851). from and to include the date, before and after leave it out: " +
            "from 1850, before 1900-05-01. Other calendars are converted: 1856-07-20 (Julian). " +
            "The Gramps Web form 1850/1/1-1860/12/31 also works. Approximate dates (about, estimated) are rejected.")]
        string? dates = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var normalizedType = objectType.Trim().ToLowerInvariant();
            if (normalizedType is not ("person" or "family" or "place"))
                throw McpToolErrors.ValidationError("Invalid objectType. Must be person, family, or place.");

            if (normalizedType is not "person" && (relatives?.Length > 0 || relativeEvents?.Length > 0))
            {
                throw McpToolErrors.ValidationError(
                    "relatives and relativeEvents are supported only for objectType person.");
            }

            var filter = TimelineDateFilter.Parse(dates);
            return normalizedType switch
            {
                "person" => await GetPersonTimelineAsync(identifier, events, relatives, relativeEvents, filter, client),
                "family" => await GetFamilyTimelineAsync(identifier, events, filter, client),
                "place" => await GetPlaceTimelineAsync(identifier, events, filter, client),
                _ => throw new InvalidOperationException("Validated timeline object type was not dispatched.")
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static async Task<string> GetPersonTimelineAsync(
        string identifier, string[]? events, string[]? relatives, string[]? relativeEvents,
        TimelineDates? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "people");
        var query = BuildQueryString(events, relatives, relativeEvents, dates?.ApiDates, includeUndated: true, personTimeline: true);
        var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
            $"/api/people/{Uri.EscapeDataString(handle)}/timeline{query}");
        if (timeline is null)
            return NotFoundHelper.NotFoundMessage("Person", identifier);
        if (timeline.Length == 0)
            return $"No timeline events found for {identifier}. " +
                   "Only linked events (and relatives per filters) appear; a name date alone is not a timeline event.";

        var others = await EventParticipantsFormatter.LoadOtherParticipantsAsync(
            client, timeline.Where(TimelineFormatter.IsOwnEventInAnotherRole).Select(e => e.Handle), handle);
        foreach (var entry in timeline)
        {
            if (TimelineFormatter.IsOwnEventInAnotherRole(entry) && entry.Handle is { } h && others.TryGetValue(h, out var text))
                entry.OtherParticipants = text;
        }

        return TimelineFormatter.FormatTimelineChronological(timeline);
    }

    private static async Task<string> GetFamilyTimelineAsync(
        string identifier, string[]? events, TimelineDates? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "families");
        var query = BuildQueryString(events, null, null, dates?.ApiDates, includeUndated: true);
        var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
            $"/api/families/{Uri.EscapeDataString(handle)}/timeline{query}");
        if (timeline is null)
            return NotFoundHelper.NotFoundMessage("Family", identifier);
        if (timeline.Length == 0)
            return $"No timeline events found for family {identifier}";
        return TimelineFormatter.FormatTimelineChronological(timeline);
    }

    private static async Task<string> GetPlaceTimelineAsync(
        string identifier, string[]? events, TimelineDates? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "places");
        // Same route as the backlinks read in CollectAsync, so the read scope serves both from one request.
        var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
            $"/api/places/{Uri.EscapeDataString(handle)}?backlinks=true");
        if (place is null)
            return NotFoundHelper.NotFoundMessage("Place", identifier);

        var outcome = await PlaceTimelineFallback.CollectAsync(client, handle, events, dates?.Range, true);
        if (outcome.MatchedPlaceCount == 0)
        {
            return $"No events linked directly to place {identifier}. " +
                   "No events reference this exact place handle in backlinks " +
                   "(events often use a city or address place, not the parent country or region).";
        }

        if (outcome.Entries.Length == 0)
        {
            return $"No events at place {identifier} match the filters (event categories and/or date range). " +
                   "Try broader categories or widen the date range.";
        }

        // Every event is at this place, so the heading names it once instead of each row.
        var id = string.IsNullOrWhiteSpace(place.GrampsId) ? "" : $" ({place.GrampsId.Trim()})";
        return TimelineFormatter.FormatTimelineChronological(
            outcome.Entries, $"Place: {GrampsValueFormatter.FormatPlace(place)}{id}");
    }

    /// <param name="apiDates">Bounds as Gramps Web takes them, from <see cref="TimelineDateFilter"/>.</param>
    /// <param name="personTimeline">
    /// Without <paramref name="apiDates"/>, keep relatives' events outside the person's first and last event.
    /// Gramps Web otherwise drops them with Gramps' fuzzy date matching, where an "about 1876" birth spans
    /// 1826 to 1926: every relative's event before 1926 was left out, the person's own marriage too.
    /// </param>
    internal static string BuildQueryString(
        string[]? events, string[]? relatives, string[]? relativeEvents,
        string? apiDates, bool includeUndated = true, bool personTimeline = false)
    {
        var queryParams = new List<string>();
        if (events?.Length > 0)
            queryParams.Add($"event_classes={Uri.EscapeDataString(string.Join(",", events))}");
        if (relatives?.Length > 0)
            queryParams.Add($"relatives={Uri.EscapeDataString(string.Join(",", relatives))}");
        if (relativeEvents?.Length > 0)
            queryParams.Add($"relative_event_classes={Uri.EscapeDataString(string.Join(",", relativeEvents))}");
        if (!string.IsNullOrEmpty(apiDates))
        {
            queryParams.Add($"dates={Uri.EscapeDataString(apiDates)}");
        }
        else if (personTimeline)
        {
            queryParams.Add("first=false");
            queryParams.Add("last=false");
        }

        if (includeUndated)
            queryParams.Add("discard_empty=false");
        return queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    }
}
