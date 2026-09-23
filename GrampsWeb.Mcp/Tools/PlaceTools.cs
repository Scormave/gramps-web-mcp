using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Place objects from the Gramps Web API.
/// Covers place mutations (browse places via list_objects('places') or search).
/// </summary>
[McpServerToolType]
public static class PlaceTools
{
    [Description(
        "Read-only: one place by handle (name, type, coordinates, hierarchy by traversing parent places). " +
        "Use when resolving place handles from events or building geographic context.")]
    internal static async Task<string> ReadPlaceAsync(
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

    [McpServerTool(Title = "Create Place", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a place (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes + " " +
        "Parent places go in enclosedBy (smaller region → larger region order as in your tree).")]
    public static async Task<string> CreatePlace(
        [Description("Primary display name (required).")]
        string name,
        [Description("Place type key. " + ToolDescriptionFragments.CallGetTypes)]
        string? placeType = null,
        [Description("Latitude coordinate")]
        string? lat = null,
        [Description("Longitude coordinate")]
        string? lon = null,
        [Description("Parent place refs (enclosure hierarchy). " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code for primary name (default: omitted)")]
        string? nameLang = null,
        [Description("Alternate place names. " + FlexiblePlaceNameList.DescriptionHint)]
        FlexiblePlaceNameList? alternateNames = null,
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

            var placeRefList = await ResolvePlaceRefListAsync((PlaceRefRequest[]?)enclosedBy, client);

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
                PlaceRefList = placeRefList,
                AltNames = (PlaceNameRequest[]?)alternateNames is { Length: > 0 } alts ? alts : null
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
        [Description("Replace parent place chain. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code for primary name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? nameLang = null,
        [Description("Replace alternate place names. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePlaceNameList.DescriptionHint)]
        FlexiblePlaceNameList? alternateNames = null,
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

            var placeRefList = enclosedBy != null
                ? await ResolvePlaceRefListAsync((PlaceRefRequest[]?)enclosedBy, client)
                : GrampsRequestMapping.ToPlaceRefRequests(place.PlaceRefList);

            var updateRequest = new CreatePlaceRequest
            {
                Class = "Place",
                Handle = place.Handle,
                GrampsId = place.GrampsId,
                Change = place.Change,
                Name = GrampsRequestMapping.ToPrimaryPlaceNameRequest(name, nameLang, place.PrimaryName),
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
                PlaceRefList = placeRefList,
                AltNames = alternateNames != null
                    ? (PlaceNameRequest[]?)alternateNames
                    : GrampsRequestMapping.ToPlaceNameRequests(place.AlternateNames),
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

    private static async Task<PlaceRefRequest[]?> ResolvePlaceRefListAsync(
        PlaceRefRequest[]? refs,
        GrampsApiClient client)
    {
        if (refs is not { Length: > 0 })
            return refs;

        var resolved = new PlaceRefRequest[refs.Length];
        for (var i = 0; i < refs.Length; i++)
        {
            var item = refs[i];
            resolved[i] = new PlaceRefRequest
            {
                Ref = await HandleResolver.ResolveToHandleAsync(item.Ref ?? "", client, "places"),
                Date = item.Date
            };
        }

        return resolved;
    }
}
