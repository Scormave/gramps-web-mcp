using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
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
    private const string NameDateHint =
        "Gramps shows the first name, primary first, whose date is empty or matches the event date, so an undated " +
        "primary name hides dated alternate names: date the primary name too (\"from 1937-02-10\"). " +
        "Dashes are spans; open \"1991-\" / \"from 1991\" are From. " +
        ToolDescriptionFragments.CallGetDateInputGuide;

    private const string LatHint = "Latitude in decimal degrees, north positive (52.2297). " + FlexibleString.DescriptionHint;
    private const string LonHint = "Longitude in decimal degrees, east positive (21.0122). " + FlexibleString.DescriptionHint;
    private const string OmitToKeepEmptyRemoves = "Omit to keep the current value; pass an empty string to remove it.";

    [Description(
        "Read-only: one place by handle (name, type, coordinates, enclosing places and full hierarchy by name). " +
        "Use when resolving place handles from events or building geographic context.")]
    internal static async Task<string> ReadPlaceAsync(
        [Description("Place handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "places");
            // The profile names the enclosing places, so the card needs no request per parent.
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                $"/api/places/{Uri.EscapeDataString(resolvedHandle)}?profile=self");
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
        [Description(LatHint)]
        FlexibleString? lat = null,
        [Description(LonHint)]
        FlexibleString? lon = null,
        [Description("Parent place refs (enclosure hierarchy). " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code for primary name (default: omitted)")]
        string? nameLang = null,
        [Description("When the primary name applies (default: always). " + NameDateHint)]
        string? nameDate = null,
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

            var nameDateRequest = AgentDateParser.ToDateRequestOrNull(nameDate, DateComponentOrder.Iso);

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
                    Lang = string.IsNullOrWhiteSpace(nameLang) ? null : nameLang.Trim(),
                    Date = nameDateRequest
                },
                Type = placeType,
                Code = code,
                Latitude = ToCoordinate(lat),
                Longitude = ToCoordinate(lon),
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate,
                PlaceRefList = placeRefList,
                AltNames = (PlaceNameRequest[]?)alternateNames is { Length: > 0 } alts ? alts : null
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/places/", request, "Place");
            return ResponseEnvelope.CreateSuccess(
                "Place", handle, grampsId,
                request.Name.Value, ResponseEnvelope.PlaceCreateNextSteps(handle!));
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
        [Description(LatHint + " " + OmitToKeepEmptyRemoves)]
        FlexibleString? lat = null,
        [Description(LonHint + " " + OmitToKeepEmptyRemoves)]
        FlexibleString? lon = null,
        [Description("Linked parent place chain. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code for primary name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? nameLang = null,
        [Description("When the primary name applies. Omit to keep the current date; pass an empty string to remove it. " + NameDateHint)]
        string? nameDate = null,
        [Description("Replace alternate place names. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePlaceNameList.DescriptionHint)]
        FlexiblePlaceNameList? alternateNames = null,
        [Description("Linked notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Place code. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? code = null,
        [Description("Linked media. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Linked citations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Linked tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            using var updateLease = await client.BeginUpdateAsync();
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
                Name = GrampsRequestMapping.ToPrimaryPlaceNameRequest(name, nameLang, nameDate, place.PrimaryName),
                Type = placeType ?? place.Type,
                Code = code ?? place.Code,
                Latitude = ToCoordinate(lat) ?? place.Latitude,
                Longitude = ToCoordinate(lon) ?? place.Longitude,
                MediaList = LinkUpdates.Apply(GrampsRequestMapping.ToMediaRefRequests(place.MediaList),
                    mediaHandles is null ? null : (GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, place.MediaList) ?? []), linkMode, x => x.Ref),
                NoteList = LinkUpdates.Apply(place.NoteList, (string[]?)noteHandles, linkMode, x => x),
                CitationList = LinkUpdates.Apply(place.CitationList, (string[]?)citationHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(place.TagList, (string[]?)tagHandles, linkMode, x => x),
                PlaceRefList = LinkUpdates.Apply(GrampsRequestMapping.ToPlaceRefRequests(place.PlaceRefList),
                    enclosedBy is null ? null : (placeRefList ?? []), linkMode, x => x.Ref),
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

    /// <summary>
    /// The coordinate text Gramps stores, trimmed and without double quotes a client wrapped around
    /// the number to make it a string; <c>null</c> when the argument was omitted.
    /// </summary>
    private static string? ToCoordinate(FlexibleString? value)
    {
        if (value is null)
            return null;
        var text = value.Value.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1].Trim();
        return text;
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
