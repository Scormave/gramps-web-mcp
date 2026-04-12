using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Place objects from the Gramps Web API.
/// Covers get_place, get_place_timeline (browse places via list_objects('places') or search).
/// </summary>
[McpServerToolType]
public static class PlaceTools
{
    [McpServerTool]
    [Description(
        "Get place data by handle. Returns name, type, coordinates, and hierarchical location " +
        "(e.g. 'Kyiv, Kyivska Oblast, Ukraine' by traversing enclosed_by). " +
        "Useful for understanding the geographic context of events.")]
    public static async Task<string> GetPlace(
        [Description("Place handle — use list_objects('places') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{handle}");
            return place == null
                ? $"Place not found: {handle}"
                : await PlaceFormatter.FormatPlaceFull(place, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get chronological timeline of events at this place. " +
        "Built from place backlinks and per-event fetches (no server place-timeline route). " +
        "Only events whose place field matches this handle are included (child vs parent places differ). " +
        "events: filter by category — vital, family, religious, vocational, academic, travel, legal, residence, other, custom " +
        "(same keywords as person/family timelines; default English type names from the Gramps Web API spec). " +
        "dates: range 'YYYY/MM/DD-YYYY/MM/DD', or open 'YYYY/MM/DD-' or '-YYYY/MM/DD'; leading zeros in month/day are normalized like other timeline tools. " +
        "include_undated: default true — when false, events with no sortable date (sortval 0 or missing) are omitted. " +
        "Rows include [event: handle] when known, for follow-up get_event calls.")]
    public static async Task<string> GetPlaceTimeline(
        [Description("Place handle")]
        string handle,
        [Description("Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom")]
        string[]? events = null,
        [Description("Date range filter; e.g. 1999/1/1-2010/12/31 (zeros normalized)")]
        string? dates = null,
        [Description("Include events with no sortable date (sortval 0 or missing); default true. Use false to match strict undated exclusion.")]
        bool includeUndated = true,
        GrampsApiClient client = null!)
    {
        try
        {
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{handle}");
            if (place == null)
                return $"Place not found: {handle}";

            var datesNormalized = PersonTools.NormalizeTimelineDatesForGrampsApi(dates);
            var outcome = await PlaceTimelineFallback.CollectAsync(
                client, handle, place, events, datesNormalized, includeUndated);

            if (outcome.MatchedPlaceCount == 0)
                return
                    $"No events linked directly to place {handle}. " +
                    "No events reference this exact place handle in backlinks " +
                    "(events often use a city or address place, not the parent country or region).";

            if (outcome.Entries.Length == 0)
                return
                    $"No events at place {handle} match the filters (event categories and/or date range). " +
                    "Try broader categories, widen the date range, or set include_undated=true if undated events were excluded.";

            return TimelineFormatter.FormatTimelineChronological(outcome.Entries);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a geographic place. Call get_types() for valid place_type values. " +
        "enclosedByHandles: parent places (village → county → country). " +
        "Returns place handle.")]
    public static async Task<string> CreatePlace(
        [Description("Place name")]
        string name,
        [Description("Place type — call get_types to get valid values")]
        string? placeType = null,
        [Description("Latitude coordinate")]
        string? lat = null,
        [Description("Longitude coordinate")]
        string? lon = null,
        [Description("Parent place handles (hierarchy). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? enclosedByHandles = null,
        [Description("Language code (default: 'en')")]
        string? nameLang = null,
        [Description("Note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Place code / postal reference (optional)")]
        string? code = null,
        [Description("Media handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var enclosed = (string[]?)enclosedByHandles;
            var placeRefList = enclosed?.Length > 0
                ? enclosed.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var request = new CreatePlaceRequest
            {
                Name = new PlaceNameRequest
                {
                    Value = name.Trim(),
                    Lang = string.IsNullOrWhiteSpace(nameLang) ? null : nameLang.Trim()
                },
                Type = placeType,
                Code = code,
                Latitude = lat,
                Longitude = lon,
                MediaList = mediaHandles,
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate,
                PlaceRefList = placeRefList
            };

            var response = await client.PostMutationAsync<GrampsPlace>("/api/places/", request, "Place");
            var typeLabel = await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, response.Type);
            return $"Place created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Name: {response.Name}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing place. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects.")]
    public static async Task<string> UpdatePlace(
        [Description("Place handle")]
        string handle,
        [Description("Update place name")]
        string? name = null,
        [Description("Update place type")]
        string? placeType = null,
        [Description("Update latitude")]
        string? lat = null,
        [Description("Update longitude")]
        string? lon = null,
        [Description("Replace parent place handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? enclosedByHandles = null,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Update place code")]
        string? code = null,
        [Description("Replace media handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Update private flag")]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{handle}");
            if (place == null)
                return $"Place not found: {handle}";

            var enclosedUpdate = (string[]?)enclosedByHandles;
            var placeRefList = enclosedUpdate != null && enclosedUpdate.Length > 0
                ? enclosedUpdate.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var updateRequest = new CreatePlaceRequest
            {
                Class = "Place",
                Handle = place.Handle,
                GrampsId = place.GrampsId,
                Change = place.Change,
                Name = new PlaceNameRequest { Value = (name ?? place.Name)?.Trim() ?? "" },
                Type = placeType ?? place.Type,
                Code = code ?? place.Code,
                Latitude = lat ?? place.Latitude,
                Longitude = lon ?? place.Longitude,
                MediaList = (string[]?)mediaHandles ?? place.MediaList,
                NoteList = (string[]?)noteHandles ?? place.NoteList,
                CitationList = (string[]?)citationHandles ?? place.CitationList,
                TagList = (string[]?)tagHandles ?? place.TagList,
                PlaceRefList = placeRefList ?? place.PlaceRefList,
                AlternateLocations = place.AlternateLocations,
                Private = isPrivate ?? place.Private
            };

            var response = await client.PutMutationAsync<GrampsPlace>($"/api/places/{handle}", updateRequest, "Place");
            var typeLabel = await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, response.Type);
            return $"Place updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Name: {response.Name ?? "—"}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a place. Will warn if referenced by events or by child places. " +
        "Deleting a parent place breaks child place hierarchy.")]
    public static async Task<string> DeletePlace(
        [Description("Place handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/places/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Place not found: {handle}";
            var response = payload.Value;

            var hasBacklinks = false;
            var backlinksInfo = new StringBuilder();
            if (response.TryGetProperty("backlinks", out var backlinksElement))
            {
                if (backlinksElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in backlinksElement.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0)
                        {
                            hasBacklinks = true;
                            backlinksInfo.AppendLine($"  • {property.Name}: {property.Value.GetArrayLength()} reference(s)");
                        }
                    }
                }
            }

            if (hasBacklinks && !force)
            {
                return $"⚠️ Cannot delete place [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_place(handle, force=true).\n" +
                       $"Warning: child places will lose parent reference.";
            }

            await client.DeleteAsync($"/api/places/{handle}");
            return $"Place deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
