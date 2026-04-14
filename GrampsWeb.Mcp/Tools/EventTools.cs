using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Event objects from the Gramps Web API.
/// Covers get_event (browse events via list_objects('events') or search).
/// </summary>
[McpServerToolType]
public static class EventTools
{
    [McpServerTool]
    [Description(
        "Read-only: one event by handle (type, date/modifiers, place, description, citations, notes, tags, media).")]
    public static async Task<string> GetEvent(
        [Description("Event handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{handle}");
            if (evt is null)
                return NotFoundHelper.NotFoundMessage("Event", handle);

            return await EventFormatter.FormatEventFull(evt, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create an event (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes + " " + ToolDescriptionFragments.CallGetDateInputGuide + " " +
        ToolDescriptionFragments.CallGetStructuredFieldInputGuide + " " +
        "Link to people/families afterward via create_person / update_person / create_family / update_family event reference lists.")]
    public static async Task<string> CreateEvent(
        [Description("Event type key from the tree. " + ToolDescriptionFragments.CallGetTypes)]
        string eventType,
        [Description("Optional event date text. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("How to read ambiguous slash/dot numeric dates. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Place handle for this event. Optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? placeHandle = null,
        [Description("Event description (optional)")]
        string? description = null,
        [Description("Citation handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Media handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. Call get_types() to see valid values.");

            var typeError = await TypeCache.ValidateTypeAsync(eventType, "event_types", client);
            if (typeError != null) throw McpToolErrors.ValidationError(typeError);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder);

            var request = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = placeHandle,
                Description = description,
                MediaList = mediaHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsEvent>("/api/events/", request, "Event");
            var typeLabel = await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, response.Type);
            return ResponseEnvelope.CreateSuccess(
                "Event", response.Handle, response.GrampsId,
                typeLabel, ResponseEnvelope.EventCreateNextSteps(response.Handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update an event (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdateEvent(
        [Description("Event handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Event type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.CallGetTypes)]
        string? eventType = null,
        [Description("Event date text. Omit to keep current. Empty string may clear per parser rules. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("Ambiguous numeric date order. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Place handle. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? placeHandle = null,
        [Description("Description text. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? description = null,
        [Description("Replace citations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace media. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (eventType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(eventType, "event_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{handle}");
            if (evt is null)
                return NotFoundHelper.NotFoundMessage("Event", handle);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder)
                : GrampsRequestMapping.ToDateRequestOrNull(evt.Date);

            var updateRequest = new CreateEventRequest
            {
                Class = "Event",
                Handle = evt.Handle,
                GrampsId = evt.GrampsId,
                Change = evt.Change,
                Type = eventType ?? evt.Type,
                Date = dateRequest,
                Place = placeHandle ?? evt.Place,
                Description = description ?? evt.Description,
                MediaList = (string[]?)mediaHandles ?? evt.MediaList,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(evt.AttributeList),
                CitationList = (string[]?)citationHandles ?? evt.CitationList,
                NoteList = (string[]?)noteHandles ?? evt.NoteList,
                TagList = (string[]?)tagHandles ?? evt.TagList,
                Private = isPrivate ?? evt.Private
            };

            var response = await client.PutMutationAsync<GrampsEvent>($"/api/events/{handle}", updateRequest, "Event");
            return ResponseEnvelope.UpdateSuccess("Event", response.Handle, response.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete an event (destructive). Blocked when people/families still reference it unless force=true.")]
    public static async Task<string> DeleteEvent(
        [Description("Event handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite backlinks (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/events/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return NotFoundHelper.NotFoundMessage("Event", handle);
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
                return $"⚠️ Cannot delete event [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_event(handle, force=true).";
            }

            await client.DeleteAsync($"/api/events/{handle}");
            return ResponseEnvelope.DeleteSuccess("Event", handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
