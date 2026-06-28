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
    [McpServerTool(Title = "Get Place", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: one place by handle (name, type, coordinates, hierarchy by traversing parent places). " +
        "Use when resolving place handles from events or building geographic context.")]
    public static async Task<string> GetPlace(
        [Description("Place handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "places");
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                $"/api/places/{Uri.EscapeDataString(resolvedHandle)}");
            return place == null
                ? NotFoundHelper.NotFoundMessage("Place", handle)
                : await PlaceFormatter.FormatPlaceFull(place, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Get Place Timeline", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: chronological events whose place field equals this handle (computed via backlinks; not a single API route). " +
        "Events on a child place (e.g. city) do not appear when querying the parent country handle. " +
        "events filters by category (same set as person timeline). dates uses YYYY/M/D ranges with zero-stripping. " +
        "Output may include event handles for get_event.")]
    public static async Task<string> GetPlaceTimeline(
        [Description("Place handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom")]
        string[]? events = null,
        [Description("Date range filter; e.g. 1999/1/1-2010/12/31 (zeros normalized)")]
        string? dates = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "places");
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                $"/api/places/{Uri.EscapeDataString(resolvedHandle)}");
            if (place == null)
                return NotFoundHelper.NotFoundMessage("Place", handle);

            var datesNormalized = PersonTools.NormalizeTimelineDatesForGrampsApi(dates);
            var outcome = await PlaceTimelineFallback.CollectAsync(
                client, resolvedHandle, place, events, datesNormalized, true);

            if (outcome.MatchedPlaceCount == 0)
                return
                    $"No events linked directly to place {handle}. " +
                    "No events reference this exact place handle in backlinks " +
                    "(events often use a city or address place, not the parent country or region).";

            if (outcome.Entries.Length == 0)
                return
                    $"No events at place {handle} match the filters (event categories and/or date range). " +
                    "Try broader categories or widen the date range.";

            return TimelineFormatter.FormatTimelineChronological(outcome.Entries);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Place", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a place (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes + " " +
        "Parent places go in enclosedByHandles (smaller region → larger region order as in your tree).")]
    public static async Task<string> CreatePlace(
        [Description("Primary display name (required).")]
        string name,
        [Description("Place type key. " + ToolDescriptionFragments.CallGetTypes)]
        string? placeType = null,
        [Description("Latitude coordinate")]
        string? lat = null,
        [Description("Longitude coordinate")]
        string? lon = null,
        [Description("Parent place handles (enclosure hierarchy). " + FlexibleHandleList.DescriptionHint)]
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

            if (placeType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(placeType, "place_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var enclosed = (string[]?)enclosedByHandles;
            if (enclosed is { Length: > 0 })
                enclosed = await ResolveHandlesAsync(enclosed, client, "places");
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
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate,
                PlaceRefList = placeRefList
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/places/", request, "Place");
            var typeLabel = await PlaceTypeDisplayFormatter.FormatStoredPlaceTypeAsync(client, placeType);
            return ResponseEnvelope.CreateSuccess(
                "Place", handle, grampsId,
                typeLabel, ResponseEnvelope.PlaceCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Place", ReadOnly = false, Destructive = false)]
    [Description(
        "Update a place (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks)]
    public static async Task<string> UpdatePlace(
        [Description("Place handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Name text. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("Place type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.CallGetTypes)]
        string? placeType = null,
        [Description("Latitude string. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? lat = null,
        [Description("Longitude string. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? lon = null,
        [Description("Parent place chain. Omit to keep unchanged. When set non-empty, replaces hierarchy; empty value does not clear parents in this API mapping—omit instead. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? enclosedByHandles = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Place code. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? code = null,
        [Description("Replace media. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace citations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (placeType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(placeType, "place_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "places");
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                $"/api/places/{Uri.EscapeDataString(resolvedHandle)}");
            if (place == null)
                return NotFoundHelper.NotFoundMessage("Place", handle);

            var enclosedUpdate = (string[]?)enclosedByHandles;
            if (enclosedUpdate is { Length: > 0 })
                enclosedUpdate = await ResolveHandlesAsync(enclosedUpdate, client, "places");
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
                MediaList = mediaHandles != null
                    ? GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, place.MediaList)
                    : GrampsRequestMapping.ToMediaRefRequests(place.MediaList),
                NoteList = (string[]?)noteHandles ?? place.NoteList,
                CitationList = (string[]?)citationHandles ?? place.CitationList,
                TagList = (string[]?)tagHandles ?? place.TagList,
                PlaceRefList = placeRefList ?? place.PlaceRefList,
                AlternateLocations = place.AlternateLocations,
                Private = isPrivate ?? place.Private
            };

            await client.PutMutationAsync($"/api/places/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Place", place.Handle, place.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Delete Place", ReadOnly = false, Destructive = true)]
    [Description(
        "Delete a place (destructive). Blocked when events or child places reference it unless force=true. " +
        "Deleting a parent can orphan child places in the hierarchy.")]
    public static async Task<string> DeletePlace(
        [Description("Place handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite references (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "places");
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Place", "places", resolvedHandle, force, handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static async Task<string[]> ResolveHandlesAsync(
        string[] handles,
        GrampsApiClient client,
        string expectedObjectType)
    {
        var resolved = new string[handles.Length];
        for (var i = 0; i < handles.Length; i++)
            resolved[i] = await HandleResolver.ResolveToHandleAsync(handles[i], client, expectedObjectType);
        return resolved;
    }
}
