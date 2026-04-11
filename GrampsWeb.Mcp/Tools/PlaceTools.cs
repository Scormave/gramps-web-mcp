using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
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
        "Loads each linked event; " +
        "only events whose place field matches this handle are included (child vs parent places differ). " +
        "Rows include [event: handle] when known, for follow-up get_event calls.")]
    public static async Task<string> GetPlaceTimeline(
        [Description("Place handle")]
        string handle,
        GrampsApiClient client = null!)
    {
        try
        {
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{handle}");
            if (place == null)
                return $"Place not found: {handle}";

            var entries = await PlaceTimelineFallback.CollectAsync(client, handle, place);
            if (entries.Length == 0)
                return
                    $"No events linked directly to place {handle}. " +
                    "No events reference this exact place handle in backlinks " +
                    "(events often use a city or address place, not the parent country or region).";

            return TimelineFormatter.FormatTimelineChronological(entries);
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
        [Description("Array of parent place handles (for geography hierarchy)")]
        string[]? enclosedByHandles = null,
        [Description("Language code (default: 'en')")]
        string? nameLang = null,
        [Description("Array of note handles")]
        string[]? noteHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var placeRefList = enclosedByHandles?.Length > 0
                ? enclosedByHandles.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var request = new CreatePlaceRequest
            {
                Name = new PlaceNameRequest
                {
                    Value = name.Trim(),
                    Lang = string.IsNullOrWhiteSpace(nameLang) ? null : nameLang.Trim()
                },
                Type = placeType,
                Latitude = lat,
                Longitude = lon,
                NoteList = noteHandles,
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
        [Description("Replace parent place handles")]
        string[]? enclosedByHandles = null,
        [Description("Replace note handles")]
        string[]? noteHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var place = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{handle}");
            if (place == null)
                return $"Place not found: {handle}";

            var placeRefList = enclosedByHandles != null && enclosedByHandles.Length > 0
                ? enclosedByHandles.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var updateRequest = new CreatePlaceRequest
            {
                Class = "Place",
                Handle = place.Handle,
                GrampsId = place.GrampsId,
                Change = place.Change,
                Name = new PlaceNameRequest { Value = (name ?? place.Name)?.Trim() ?? "" },
                Type = placeType ?? place.Type,
                Code = place.Code,
                Latitude = lat ?? place.Latitude,
                Longitude = lon ?? place.Longitude,
                MediaList = place.MediaList,
                NoteList = noteHandles ?? place.NoteList,
                CitationList = place.CitationList,
                TagList = place.TagList,
                PlaceRefList = placeRefList ?? place.PlaceRefList,
                AlternateLocations = place.AlternateLocations,
                Private = place.Private
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
