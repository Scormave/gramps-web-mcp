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
        "Get event data by handle. Returns event type, date with modifiers (before/after/about), " +
        "place handle and name, description, linked citations and notes. " +
        "Use list_objects('events') or search() to find event handles.")]
    public static async Task<string> GetEvent(
        [Description("Event handle — use list_objects('events') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{handle}");
            if (evt is null)
                return $"Event not found: {handle}";

            return await EventFormatter.FormatEventFull(evt, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a life event. Call get_types() for valid event_type values. " +
        "After creating, link to person via update_person(eventRefHandles) or include in create_person(eventRefHandles). " +
        "Returns event handle. Dates: prefer ISO yyyy-MM-dd; set dateComponentOrder for dd/MM/yyyy vs MM/dd/yyyy. " +
        "Full syntax: get_date_input_guide().")]
    public static async Task<string> CreateEvent(
        [Description("Event type — must call get_types to get valid values")]
        string eventType,
        [Description("Event date as text (optional). Formats: get_date_input_guide().")]
        string? date = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Place handle (optional)")]
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
        [Description("Custom attributes (type + value)")]
        GrampsAttribute[]? attributes = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. Call get_types() to see valid values.");

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder);

            var request = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = placeHandle,
                Description = description,
                MediaList = mediaHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests(attributes),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsEvent>("/api/events/", request, "Event");
            var dateStr = response.Date != null ? GrampsValueFormatter.FormatDate(response.Date) : "—";
            var typeLabel = await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, response.Type);
            return $"Event created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Type: {typeLabel}\n" +
                   $"Date: {dateStr}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing event. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects. " +
        "Date: same as create_event; omit date to keep existing. Reference: get_date_input_guide().")]
    public static async Task<string> UpdateEvent(
        [Description("Event handle")]
        string handle,
        [Description("Update event type")]
        string? eventType = null,
        [Description("Update event date as text (optional). Empty string clears. Formats: get_date_input_guide().")]
        string? date = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Update place handle")]
        string? placeHandle = null,
        [Description("Update description")]
        string? description = null,
        [Description("Replace citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace media handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace attributes (omit to keep; [] clears)")]
        GrampsAttribute[]? attributes = null,
        [Description("Update private flag")]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{handle}");
            if (evt is null)
                return $"Event not found: {handle}";

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
                    ? GrampsRequestMapping.ToAttributeRequests(attributes)
                    : GrampsRequestMapping.ToAttributeRequests(evt.AttributeList),
                CitationList = (string[]?)citationHandles ?? evt.CitationList,
                NoteList = (string[]?)noteHandles ?? evt.NoteList,
                TagList = (string[]?)tagHandles ?? evt.TagList,
                Private = isPrivate ?? evt.Private
            };

            var response = await client.PutMutationAsync<GrampsEvent>($"/api/events/{handle}", updateRequest, "Event");
            var typeLabel = await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, response.Type);
            return $"Event updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete an event. Will warn if referenced in person or family event lists.")]
    public static async Task<string> DeleteEvent(
        [Description("Event handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/events/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Event not found: {handle}";
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
            return $"Event deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
