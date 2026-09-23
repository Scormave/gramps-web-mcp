using System.ComponentModel;
using GrampsWeb.Mcp.Client;
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
        "relatives and relativeEvents are supported only for person timelines. Place timelines are computed from direct event backlinks; child places are not included.")]
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
        [Description("Date range; e.g. 1999/1/1-2010/12/31. Leading zeros are normalized for the Gramps Web API.")]
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

            return normalizedType switch
            {
                "person" => await GetPersonTimelineAsync(identifier, events, relatives, relativeEvents, dates, client),
                "family" => await GetFamilyTimelineAsync(identifier, events, dates, client),
                "place" => await GetPlaceTimelineAsync(identifier, events, dates, client),
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
        string? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "people");
        var query = BuildQueryString(events, relatives, relativeEvents, dates, includeUndated: true);
        var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
            $"/api/people/{Uri.EscapeDataString(handle)}/timeline{query}");
        if (timeline is null)
            return NotFoundHelper.NotFoundMessage("Person", identifier);
        if (timeline.Length == 0)
            return $"No timeline events found for {identifier}. " +
                   "Only linked events (and relatives per filters) appear; a name date alone is not a timeline event.";
        return TimelineFormatter.FormatTimelineChronological(timeline);
    }

    private static async Task<string> GetFamilyTimelineAsync(
        string identifier, string[]? events, string? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "families");
        var query = BuildQueryString(events, null, null, dates, includeUndated: true);
        var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
            $"/api/families/{Uri.EscapeDataString(handle)}/timeline{query}");
        if (timeline is null)
            return NotFoundHelper.NotFoundMessage("Family", identifier);
        if (timeline.Length == 0)
            return $"No timeline events found for family {identifier}";
        return TimelineFormatter.FormatTimelineChronological(timeline);
    }

    private static async Task<string> GetPlaceTimelineAsync(
        string identifier, string[]? events, string? dates, GrampsApiClient client)
    {
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, "places");
        var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
            $"/api/places/{Uri.EscapeDataString(handle)}");
        if (place is null)
            return NotFoundHelper.NotFoundMessage("Place", identifier);

        var datesNormalized = NormalizeDatesForGrampsApi(dates);
        var outcome = await PlaceTimelineFallback.CollectAsync(client, handle, place, events, datesNormalized, true);
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

        return TimelineFormatter.FormatTimelineChronological(outcome.Entries);
    }

    internal static string BuildQueryString(
        string[]? events, string[]? relatives, string[]? relativeEvents,
        string? dates, bool includeUndated = true)
    {
        var queryParams = new List<string>();
        if (events?.Length > 0)
            queryParams.Add($"event_classes={Uri.EscapeDataString(string.Join(",", events))}");
        if (relatives?.Length > 0)
            queryParams.Add($"relatives={Uri.EscapeDataString(string.Join(",", relatives))}");
        if (relativeEvents?.Length > 0)
            queryParams.Add($"relative_event_classes={Uri.EscapeDataString(string.Join(",", relativeEvents))}");
        var normalizedDates = NormalizeDatesForGrampsApi(dates);
        if (!string.IsNullOrEmpty(normalizedDates))
            queryParams.Add($"dates={Uri.EscapeDataString(normalizedDates)}");
        if (includeUndated)
            queryParams.Add("discard_empty=false");
        return queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    }

    internal static string? NormalizeDatesForGrampsApi(string? dates)
    {
        if (string.IsNullOrWhiteSpace(dates))
            return dates;

        var value = dates.Trim();
        if (value.StartsWith("-", StringComparison.Ordinal))
            return "-" + NormalizeYmdSegment(value[1..]);
        if (value.EndsWith("-", StringComparison.Ordinal) && !value[..^1].Contains('-', StringComparison.Ordinal))
            return NormalizeYmdSegment(value[..^1]) + "-";

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && dash < value.Length - 1)
            return $"{NormalizeYmdSegment(value[..dash])}-{NormalizeYmdSegment(value[(dash + 1)..])}";

        return NormalizeYmdSegment(value);
    }

    private static string NormalizeYmdSegment(string segment)
    {
        var parts = segment.Split('/');
        if (parts.Length != 3 || parts.Any(part => part.Contains('*', StringComparison.Ordinal)))
            return segment;

        return int.TryParse(parts[0], out var year)
               && int.TryParse(parts[1], out var month)
               && int.TryParse(parts[2], out var day)
            ? $"{year}/{month}/{day}"
            : segment;
    }
}
