using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
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

            var linkedPeople = await CollectLinkedPeopleAsync(handle, client);
            return await EventFormatter.FormatEventFull(evt, client, linkedPeople);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static async Task<IReadOnlyList<(string Handle, string? DisplayName)>> CollectLinkedPeopleAsync(
        string eventHandle,
        GrampsApiClient client)
    {
        var raw = await client.GetJsonOrNullIfNotFoundAsync($"/api/events/{Uri.EscapeDataString(eventHandle)}?backlinks=true");
        if (raw is not { } root || root.ValueKind != JsonValueKind.Object)
            return [];

        if (!root.TryGetProperty("backlinks", out var backlinks) || backlinks.ValueKind != JsonValueKind.Object)
            return [];

        var handles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in new[] { "person", "people" })
        {
            if (!backlinks.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var el in arr.EnumerateArray())
            {
                var h = HandleElementReader.ReadHandleFromElement(el).Trim();
                if (h.Length > 0)
                    handles.Add(h);
            }
        }

        if (handles.Count == 0)
            return [];

        var result = new List<(string Handle, string? DisplayName)>(handles.Count);
        foreach (var h in handles.OrderBy(static x => x, StringComparer.Ordinal))
        {
            string? displayName = null;
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(h)}");
            if (person?.PrimaryName != null)
                displayName = GrampsValueFormatter.FormatName(person.PrimaryName);
            result.Add((h, displayName));
        }

        return result;
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
                throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

            var typeError = await TypeCache.ValidateTypeAsync(eventType, "event_types", client);
            if (typeError != null) throw McpToolErrors.ValidationError(typeError);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso);

            var request = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = placeHandle,
                Description = description,
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/events/", request, "Event");
            var typeLabel = await GrampsDefaultTypeLabels.FormatEventTypeAsync(client, eventType);
            return ResponseEnvelope.CreateSuccess(
                "Event", handle, grampsId,
                typeLabel, ResponseEnvelope.EventCreateNextSteps(handle!));
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
                ? AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso)
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
                MediaList = mediaHandles != null
                    ? GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, evt.MediaList)
                    : GrampsRequestMapping.ToMediaRefRequests(evt.MediaList),
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(evt.AttributeList),
                CitationList = (string[]?)citationHandles ?? evt.CitationList,
                NoteList = (string[]?)noteHandles ?? evt.NoteList,
                TagList = (string[]?)tagHandles ?? evt.TagList,
                Private = isPrivate ?? evt.Private
            };

            await client.PutMutationAsync($"/api/events/{handle}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Event", evt.Handle, evt.GrampsId);
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
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Event", "events", handle, force);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
