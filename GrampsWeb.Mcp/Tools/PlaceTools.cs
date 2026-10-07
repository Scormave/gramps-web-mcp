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
        "Dashes are spans; open \"1991-\" / \"from 1991\" are From. Unreadable dates are rejected.";

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
        "Create a place (village, town, parish, county, country, …) with its name, type, coordinates, enclosing places, " +
        "historical or other-language names, and links to notes, media, citations and tags. " +
        "Returns the new handle, Gramps ID and name, with next steps. Does not check for duplicates: search for the place first, " +
        "and change an existing one with update_place. Enclosing places must already exist: create the larger region first " +
        "and pass it in enclosedBy, with dates when the place changed hands. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreatePlace(
        [Description("Primary name (required).")]
        string name,
        [Description("Place type, e.g. Country, Province, County, City, Town, Village, Hamlet, Parish. " + ToolDescriptionFragments.KnownType)]
        string? placeType = null,
        [Description(LatHint)]
        FlexibleString? lat = null,
        [Description(LonHint)]
        FlexibleString? lon = null,
        [Description("Places that contain this one. " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code of the primary name, e.g. pl or de (optional).")]
        string? nameLang = null,
        [Description("When the primary name applies (default: always). " + NameDateHint)]
        string? nameDate = null,
        [Description("Other names: historical, other languages. " + FlexiblePlaceNameList.DescriptionHint)]
        FlexiblePlaceNameList? alternateNames = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Place code or postal code (optional).")]
        string? code = null,
        [Description("Media. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citations. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark the place private (default false).")]
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
        "Change an existing place: name with its language and date, type, coordinates, enclosing places, alternate names, code, " +
        "and links to notes, media, citations and tags. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing place returns a not-found message. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdatePlace(
        [Description("The place to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New primary name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("New place type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? placeType = null,
        [Description(LatHint + " " + OmitToKeepEmptyRemoves)]
        FlexibleString? lat = null,
        [Description(LonHint + " " + OmitToKeepEmptyRemoves)]
        FlexibleString? lon = null,
        [Description("Places that contain this one. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexiblePlaceRefList.DescriptionHint)]
        FlexiblePlaceRefList? enclosedBy = null,
        [Description("Language code of the primary name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? nameLang = null,
        [Description("When the primary name applies. Omit to keep the current date; pass an empty string to remove it. " + NameDateHint)]
        string? nameDate = null,
        [Description("Alternate names. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexiblePlaceNameList.DescriptionHint)]
        FlexiblePlaceNameList? alternateNames = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("New place code. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? code = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
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
            var place = await GrampsObjectPatch.LoadAsync(client, $"/api/places/{Uri.EscapeDataString(resolvedHandle)}");
            if (place == null)
                return NotFoundHelper.NotFoundMessage("Place", handle);

            if (name != null || nameLang != null || nameDate != null)
            {
                var storedName = GrampsObjectPatch.StringValue(place.Root["name"]);
                var primary = place.Object("name");
                if (name != null)
                    primary["value"] = name.Trim();
                else if (primary["value"] is null)
                    primary["value"] = storedName ?? "";
                if (nameLang != null)
                    primary["lang"] = nameLang.Trim();
                if (nameDate != null)
                {
                    var dateRequest = AgentDateParser.ToDateRequestOrNull(nameDate, DateComponentOrder.Iso);
                    if (dateRequest is null)
                        primary.Remove("date");
                    else
                        primary["date"] = GrampsObjectPatch.ToNode(dateRequest);
                }
            }

            place.Set("place_type", placeType);
            place.Set("code", code);
            place.Set("lat", ToCoordinate(lat));
            place.Set("long", ToCoordinate(lon));
            if (enclosedBy != null)
                place.ApplyRefs("placeref_list", await ResolvePlaceRefListAsync((PlaceRefRequest[]?)enclosedBy, client) ?? [], linkMode);
            place.Set("alt_names", (PlaceNameRequest[]?)alternateNames);
            place.ApplyMediaHandles(mediaHandles, linkMode);
            place.ApplyHandles("note_list", noteHandles, linkMode);
            place.ApplyHandles("citation_list", citationHandles, linkMode);
            place.ApplyHandles("tag_list", tagHandles, linkMode);
            place.Set("private", isPrivate);

            await place.SaveAsync(client);
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
