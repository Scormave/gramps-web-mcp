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
/// Covers event mutations (browse events via list_objects('events') or search).
/// </summary>
[McpServerToolType]
public static class EventTools
{
    [Description(
        "Read-only: one event by handle (type, date/modifiers, place, description, citations, notes, tags, media).")]
    internal static async Task<string> ReadEventAsync(
        [Description("Event handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "events");
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>(
                $"/api/events/{Uri.EscapeDataString(resolvedHandle)}");
            if (evt is null)
                return NotFoundHelper.NotFoundMessage("Event", handle);

            var linkedPeople = await CollectLinkedPeopleAsync(resolvedHandle, client);
            return await EventFormatter.FormatEventFull(evt, client, linkedPeople);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static async Task<IReadOnlyList<(string Handle, string? DisplayName, string Role)>> CollectLinkedPeopleAsync(
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

        var result = new List<(string Handle, string? DisplayName, string Role)>(handles.Count);
        foreach (var h in handles.OrderBy(static x => x, StringComparer.Ordinal))
        {
            string? displayName = null;
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(h)}");
            if (person?.PrimaryName != null)
                displayName = GrampsValueFormatter.FormatName(person.PrimaryName);
            var role = ResolveDistinctRolesForPersonEvent(person, eventHandle);
            result.Add((h, displayName, role));
        }

        return result;
    }

    /// <summary>
    /// Distinct roles from a person's event refs targeting <paramref name="eventHandle"/>
    /// (<see cref="StringComparison.Ordinal"/> on <see cref="GrampsEventRef.Ref"/>).
    /// Empty roles default to Primary; multiple refs yield one entry per distinct role.
    /// </summary>
    internal static string ResolveDistinctRolesForPersonEvent(GrampsPerson? person, string eventHandle)
    {
        if (person?.EventRefList is not { Length: > 0 } list)
            return "Primary";

        static string NormalizeRole(string? role) =>
            string.IsNullOrWhiteSpace(role) ? "Primary" : role.Trim();

        var orderedDistinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var er in list)
        {
            if (string.IsNullOrEmpty(er.Ref) || !string.Equals(er.Ref, eventHandle, StringComparison.Ordinal))
                continue;
            var r = NormalizeRole(er.Role);
            if (seen.Add(r))
                orderedDistinct.Add(r);
        }

        return orderedDistinct.Count > 0 ? string.Join(", ", orderedDistinct) : "Primary";
    }

    [McpServerTool(Title = "Create Event", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a standalone event (Birth, Marriage, Residence, Census, …) with its date, place, description, citations, " +
        "notes and media. Returns the new handle, Gramps ID and type, with next steps. The event is linked to nobody yet: " +
        "attach it with update_person or update_family (eventRefs, linkMode add). To create an event and attach it to one person " +
        "in a single call use add_event_to_person; change an existing event with update_event. " +
        "An unknown type or unreadable date is rejected before anything is saved. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateEvent(
        [Description("Event type, e.g. Birth, Baptism, Death, Burial, Marriage, Residence, Occupation. " + ToolDescriptionFragments.KnownType)]
        string eventType,
        [Description("When it happened, optional. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Where it happened, optional; the place must exist (create_place). " + ToolDescriptionFragments.HandleDiscovery)]
        string? placeHandle = null,
        [Description("Short description, optional.")]
        string? description = null,
        [Description("Citations supporting the event. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Media. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark the event private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

            var typeError = await TypeCache.ValidateTypeAsync(eventType, "event_types", client);
            if (typeError != null) throw McpToolErrors.ValidationError(typeError);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
            var resolvedPlaceHandle = placeHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(placeHandle, client, "places");

            var request = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = resolvedPlaceHandle,
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

    [McpServerTool(Title = "Update Event", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing event: type, date, place, description, citations, notes, media, tags, attributes or the private flag. " +
        ToolDescriptionFragments.UpdateSemantics + " Returns the handle and Gramps ID; a missing event returns a not-found message, " +
        "and an unknown type or unreadable date is rejected before anything is saved. Who takes part is stored on the people " +
        "and families (their eventRefs), not on the event. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateEvent(
        [Description("The event to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New event type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? eventType = null,
        [Description("New date. Omit to keep the current date; pass an empty string to remove it. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("New place. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? placeHandle = null,
        [Description("New description. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? description = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
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
            if (eventType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(eventType, "event_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "events");
            var resolvedPlaceHandle = placeHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(placeHandle, client, "places");
            var evt = await client.GetOrNullIfNotFoundAsync<GrampsEvent>(
                $"/api/events/{Uri.EscapeDataString(resolvedHandle)}");
            if (evt is null)
                return NotFoundHelper.NotFoundMessage("Event", handle);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range)
                : GrampsRequestMapping.ToDateRequestOrNull(evt.Date);

            var updateRequest = new CreateEventRequest
            {
                Class = "Event",
                Handle = evt.Handle,
                GrampsId = evt.GrampsId,
                Change = evt.Change,
                Type = eventType ?? evt.Type,
                Date = dateRequest,
                Place = resolvedPlaceHandle ?? evt.Place,
                Description = description ?? evt.Description,
                MediaList = LinkUpdates.Apply(GrampsRequestMapping.ToMediaRefRequests(evt.MediaList),
                    mediaHandles is null ? null : (GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, evt.MediaList) ?? []), linkMode, x => x.Ref),
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(evt.AttributeList),
                CitationList = LinkUpdates.Apply(evt.CitationList, (string[]?)citationHandles, linkMode, x => x),
                NoteList = LinkUpdates.Apply(evt.NoteList, (string[]?)noteHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(evt.TagList, (string[]?)tagHandles, linkMode, x => x),
                Private = isPrivate ?? evt.Private
            };

            await client.PutMutationAsync($"/api/events/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Event", evt.Handle, evt.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
