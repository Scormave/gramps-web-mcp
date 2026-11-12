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

    private static async Task<IReadOnlyList<(string Handle, string? DisplayName, string Role, string? LinkAttributes)>> CollectLinkedPeopleAsync(
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

        var result = new List<(string Handle, string? DisplayName, string Role, string? LinkAttributes)>(handles.Count);
        foreach (var h in handles.OrderBy(static x => x, StringComparer.Ordinal))
        {
            string? displayName = null;
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(h)}");
            if (person?.PrimaryName != null)
                displayName = GrampsValueFormatter.FormatName(person.PrimaryName);
            var role = ResolveDistinctRolesForPersonEvent(person, eventHandle);
            result.Add((h, displayName, role, ResolveLinkAttributesForPersonEvent(person, eventHandle)));
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

    /// <summary>
    /// The attributes of a person's links to <paramref name="eventHandle"/>, such as their Age, on one line;
    /// null when the links have none.
    /// </summary>
    internal static string? ResolveLinkAttributesForPersonEvent(GrampsPerson? person, string eventHandle)
    {
        var parts = (person?.EventRefList ?? [])
            .Where(er => string.Equals(er.Ref, eventHandle, StringComparison.Ordinal))
            .Select(er => AttributeListFormatter.FormatInline(er.AttributeList))
            .OfType<string>()
            .ToArray();
        return parts.Length == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>The checked request body of a new event for create_records.</summary>
    internal static async Task<CreateEventRequest> BuildEventRequestAsync(
        string eventType, string? date, string? placeHandle, string? description,
        FlexibleHandleList? citationHandles, FlexibleHandleList? noteHandles, FlexibleHandleList? tagHandles,
        FlexibleMediaLinkList? mediaHandles, FlexibleAttributeList? attributes, bool isPrivate,
        GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

        eventType = await KnownTypes.ResolveAsync(eventType, "event_types", client);
        await KnownTypes.CheckAttributesAsync(attributes, client);
        await KnownTypes.CheckMediaLinksAsync(mediaHandles, client);

        var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
        await new LinkIds()
            .One("placeHandle", placeHandle, "places", h => placeHandle = h)
            .Handles("citationHandles", citationHandles, "citations")
            .Handles("noteHandles", noteHandles, "notes")
            .Handles("tagHandles", tagHandles, "tags")
            .MediaLinks("mediaHandles", mediaHandles)
            .Attributes("attributes", attributes)
            .ResolveAsync(client);

        return new CreateEventRequest
        {
            Type = eventType,
            Date = dateRequest,
            Place = placeHandle,
            Description = description,
            MediaList = GrampsRequestMapping.ToMediaRefRequests(mediaHandles),
            AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
            CitationList = citationHandles,
            NoteList = noteHandles,
            TagList = tagHandles,
            Private = isPrivate
        };
    }

    [McpServerTool(Title = "Update Event", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing event's type, date, place, description, attributes or links to citations, notes, media and tags; " +
        "everyone who shares the event sees the change. Who takes part is not set here: add or remove the event in a person's " +
        "or family's eventRefs with update_person or update_family, which also hold each participant's role. " +
        "For a new event use create_records; to remove one, delete_records. An empty string clears date, and attributes " +
        "replace the whole list whatever linkMode says. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing event returns a not-found message, and an unknown type or unreadable " +
        "date is rejected before anything is saved. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
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
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.TagHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleMediaLinkList.DescriptionHint + " " + FlexibleMediaLinkList.UpdateHint)]
        FlexibleMediaLinkList? mediaHandles = null,
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
            await KnownTypes.CheckAttributesAsync(attributes, client);
            await KnownTypes.CheckMediaLinksAsync(mediaHandles, client, linkMode);
            await new LinkIds()
                .One("placeHandle", placeHandle, "places", h => placeHandle = h)
                .Handles("citationHandles", citationHandles, "citations")
                .Handles("noteHandles", noteHandles, "notes")
                .Handles("tagHandles", tagHandles, "tags")
                .MediaLinks("mediaHandles", mediaHandles)
                .Attributes("attributes", attributes)
                .ResolveAsync(client, "Nothing was changed.");
            using var updateLease = await client.BeginUpdateAsync();
            if (eventType != null)
                eventType = await KnownTypes.ResolveAsync(eventType, "event_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "events");
            var evt = await GrampsObjectPatch.LoadAsync(client, $"/api/events/{Uri.EscapeDataString(resolvedHandle)}");
            if (evt is null)
                return NotFoundHelper.NotFoundMessage("Event", handle);

            evt.Set("type", eventType);
            if (date != null)
                evt.SetOrRemove("date", AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range));
            evt.Set("place", placeHandle);
            evt.Set("description", description);
            evt.ApplyMediaLinks(mediaHandles, linkMode);
            evt.ReplaceAttributes(attributes);
            evt.ApplyHandles("citation_list", citationHandles, linkMode);
            evt.ApplyHandles("note_list", noteHandles, linkMode);
            evt.ApplyHandles("tag_list", tagHandles, linkMode);
            evt.Set("private", isPrivate);

            await evt.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Event", evt.Handle, evt.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
