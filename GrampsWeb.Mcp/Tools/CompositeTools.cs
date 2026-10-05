using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools.Parsing;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Composite / convenience MCP tools that combine multiple API calls into single operations,
/// reducing the number of sequential tool calls an agent needs to make.
/// </summary>
[McpServerToolType]
public static class CompositeTools
{
    // ─────────────────────────────────────────────────────────────────────────
    // QuickAddPerson
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Title = "Quick Add Person", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a person with their birth and death in one call: makes the Birth and Death events, reuses each place " +
        "whose name matches exactly (ignoring case) or creates it, and links the events to the new person. " +
        "Returns the person's handle, Gramps ID, name and gender, the birth and death, every other object created, and next steps. " +
        "Does not check for duplicate people: search for the person first. Steps are not rolled back: if one fails, " +
        "the error lists the objects already created, so continue from those instead of calling again. " +
        "For other names, events, families or citations use create_person, add_event_to_person and update_person.")]
    public static async Task<string> QuickAddPerson(
        [Description("Full name: 'Given Surname', where the last word is the surname, or 'Given|Surname' to split it yourself, e.g. 'John Smith', 'Mary Ann|Van Dyke'.")]
        string name,
        [Description("Female, Male or Unknown (default Unknown); anything else is rejected.")]
        string gender = "Unknown",
        [Description("Birth date, optional. " + ToolDescriptionFragments.DateText)]
        string? birthDate = null,
        [Description("Birth place name, e.g. London (optional). The place with exactly this name is reused, otherwise a new place without type or region is created.")]
        string? birthPlace = null,
        [Description("Death date, optional. " + ToolDescriptionFragments.DateText)]
        string? deathDate = null,
        [Description("Death place name (optional), matched like birthPlace.")]
        string? deathPlace = null,
        GrampsApiClient client = null!)
    {
        var createdObjects = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var parsedName = FlexibleGrampsNameParsing.ParseSimpleLine(name);
            var genderCode = GrampsGenderParser.ParseRequired(gender);
            var summary = new StringBuilder();

            // Resolve or create places
            var birthPlaceResult = await ResolveOrCreatePlaceAsync(birthPlace, client);
            var deathPlaceResult = await ResolveOrCreatePlaceAsync(deathPlace, client);

            if (birthPlaceResult != null)
                createdObjects.Add(FormatPlaceCreationNote(birthPlaceResult));
            if (deathPlaceResult != null)
                createdObjects.Add(FormatPlaceCreationNote(deathPlaceResult));

            // Create events
            var eventHandles = new List<string>();

            (string? Handle, string? GrampsId)? birthEvent = null;
            if (birthDate != null || birthPlaceResult != null)
            {
                birthEvent = await CreateEventAsync(
                    "Birth", birthDate, birthPlaceResult?.Handle, client);
                if (birthEvent.Value.Handle != null) eventHandles.Add(birthEvent.Value.Handle);
                createdObjects.Add($"Event (Birth): {birthEvent.Value.GrampsId} (handle: {birthEvent.Value.Handle})");
            }

            (string? Handle, string? GrampsId)? deathEvent = null;
            if (deathDate != null || deathPlaceResult != null)
            {
                deathEvent = await CreateEventAsync(
                    "Death", deathDate, deathPlaceResult?.Handle, client);
                if (deathEvent.Value.Handle != null) eventHandles.Add(deathEvent.Value.Handle);
                createdObjects.Add($"Event (Death): {deathEvent.Value.GrampsId} (handle: {deathEvent.Value.Handle})");
            }

            // Build event ref list
            var eventRefList = eventHandles.Count > 0
                ? GrampsRequestMapping.BuildEventRefList(
                    eventHandles.ToArray(),
                    eventHandles.Select(_ => "Primary").ToArray())
                : null;

            // Create person
            var nameRequest = new GrampsNameRequest
            {
                FirstName = parsedName.FirstName,
                SurnameList = parsedName.SurnameList?.Select(s => new SurnameRequest
                {
                    Surname = s.Surname,
                    Primary = s.Primary
                }).ToArray()
            };

            var personRequest = new CreatePersonRequest
            {
                Gender = genderCode,
                PrimaryName = nameRequest,
                EventRefList = eventRefList
            };

            var (personHandle, personGrampsId) = await client.PostMutationAsync("/api/people/", personRequest, "Person");
            createdObjects.Insert(0, $"Person: {personGrampsId} (handle: {personHandle})");

            // Build output
            var displayName = GrampsValueFormatter.FormatName(parsedName);
            var genderLabel = genderCode switch { 0 => "Female", 1 => "Male", _ => "Unknown" };

            var birthLine = FormatVitalLine(birthDate, birthPlaceResult, birthEvent);
            var deathLine = FormatVitalLine(deathDate, deathPlaceResult, deathEvent);

            summary.AppendLine("---");
            summary.AppendLine("type: Person");
            summary.AppendLine("action: created");
            summary.AppendLine($"handle: {personHandle}");
            summary.AppendLine($"gramps_id: {personGrampsId}");
            summary.AppendLine($"name: {displayName}");
            summary.AppendLine($"gender: {genderLabel}");
            summary.AppendLine("---");
            if (birthLine != "\u2014")
                summary.AppendLine($"Birth: {birthLine}");
            if (deathLine != "\u2014")
                summary.AppendLine($"Death: {deathLine}");
            if (createdObjects.Count > 1)
            {
                summary.AppendLine();
                summary.AppendLine("Related objects created:");
                foreach (var obj in createdObjects.Skip(1))
                    summary.AppendLine($"  • {obj}");
            }

            var nextSteps = ResponseEnvelope.PersonCreateNextSteps(personHandle,
                hasBirth: birthEvent != null, hasDeath: deathEvent != null);
            summary.AppendLine();
            summary.AppendLine("Next steps:");
            foreach (var step in nextSteps)
                summary.AppendLine($"  • {step}");

            return summary.ToString();
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex, createdObjects);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AddEventToPerson
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool(Title = "Add Event To Person", ReadOnly = false, Destructive = false)]
    [Description(
        "Create an event and attach it to one existing person in one call, keeping their other events. " +
        "The place may be an existing place's Gramps ID or handle, or a name: the place with exactly that name (ignoring case) is reused, otherwise it is created. " +
        "Returns the event's handle, Gramps ID and type, the person it was attached to, and any place created. " +
        "Steps are not rolled back: if attaching fails, the error lists the event and place already created, so attach those instead of calling again. " +
        "For an event shared by several people (a marriage, a baptism with godparents) or with citations, use create_event, then " +
        "update_person or update_family with linkMode add for each participant.")]
    public static async Task<string> AddEventToPerson(
        [Description("The person the event belongs to. " + ToolDescriptionFragments.HandleDiscovery)]
        string personHandle,
        [Description("Event type, e.g. Birth, Baptism, Death, Burial, Residence, Occupation. " +
                     "A type the tree does not know is saved as a new custom type, so take the spelling from get_reference(topic: \"types\", section: \"event_types\").")]
        string eventType,
        [Description("When it happened, optional. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Where it happened (optional): a place's Gramps ID or handle, which must exist, or a name, which reuses the place with exactly that name (ignoring case) or creates a new one.")]
        string? place = null,
        [Description("Short description of the event (optional).")]
        string? description = null,
        [Description("The person's role in the event: Primary (default), Witness, Godparent, Informant, …")]
        string role = "Primary",
        GrampsApiClient client = null!)
    {
        var createdObjects = new List<string>();
        try
        {
            using var updateLease = await client.BeginUpdateAsync();
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

            // Resolve person handle
            var resolvedPersonHandle = await HandleResolver.ResolveToHandleAsync(personHandle, client, "people");

            // Fetch existing person
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>(
                $"/api/people/{Uri.EscapeDataString(resolvedPersonHandle)}");
            if (person is null)
                return NotFoundHelper.NotFoundMessage("Person", personHandle);

            // Resolve or create place. A Gramps ID or handle must name an existing place, so a
            // mistyped one is reported instead of becoming the name of a new place.
            PlaceResult? placeResult = null;
            if (!string.IsNullOrWhiteSpace(place))
            {
                place = place.Trim();
                if (HandleResolver.LooksLikeGrampsId(place) || HandleResolver.LooksLikeHandle(place))
                {
                    var placeHandle = await HandleResolver.ResolveToHandleAsync(place, client, "places");
                    var existingPlace = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                        $"/api/places/{Uri.EscapeDataString(placeHandle)}");
                    if (existingPlace is null)
                        return NotFoundHelper.NotFoundMessage("Place", place);
                    placeResult = new PlaceResult(existingPlace.Handle!, existingPlace.GrampsId, existingPlace.Name, true);
                }
                else
                {
                    placeResult = await ResolveOrCreatePlaceAsync(place, client);
                }

                if (placeResult != null)
                    createdObjects.Add(FormatPlaceCreationNote(placeResult));
            }

            // Create event
            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
            var eventRequest = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = placeResult?.Handle,
                Description = description
            };

            var (eventHandle, eventGrampsId) = await client.PostMutationAsync("/api/events/", eventRequest, "Event");
            createdObjects.Add($"Event ({eventType}): {eventGrampsId} (handle: {eventHandle})");

            // Build updated event ref list by appending new event
            var existingRefs = GrampsRequestMapping.ToEventRefRequests(person.EventRefList)
                               ?? Array.Empty<EventRefRequest>();
            var updatedRefs = existingRefs.Append(new EventRefRequest
            {
                Ref = eventHandle,
                Role = role
            }).ToArray();

            // Build person update preserving all existing fields
            var updateRequest = new CreatePersonRequest
            {
                Class = "Person",
                Handle = person.Handle,
                GrampsId = person.GrampsId,
                Change = person.Change,
                Gender = person.Gender,
                PrimaryName = person.PrimaryName != null ? PersonTools.ConvertNameToRequest(person.PrimaryName) : null,
                AlternateNames = person.AlternateNames?.Select(PersonTools.ConvertNameToRequest).ToArray(),
                EventRefList = updatedRefs,
                FamilyList = person.FamilyList,
                ParentFamilyList = GrampsRequestMapping.ToParentFamilyHandles(person.ParentFamilyList),
                MediaList = GrampsRequestMapping.ToMediaRefRequests(person.MediaList),
                AddressList = person.AddressList,
                AttributeList = GrampsRequestMapping.ToAttributeRequests(person.AttributeList),
                CitationList = person.CitationList,
                NoteList = person.NoteList,
                TagList = person.TagList,
                UrlList = person.UrlList,
                PersonRefList = person.PersonRefList,
                Private = person.Private
            };

            await client.PutMutationAsync(
                $"/api/people/{Uri.EscapeDataString(person.Handle!)}", updateRequest);

            // Build output
            var personName = person.PrimaryName != null
                ? GrampsValueFormatter.FormatName(person.PrimaryName)
                : "Unknown";
            var dateStr = date ?? "\u2014";
            var placeStr = placeResult?.Name ?? "\u2014";

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: Event");
            sb.AppendLine("action: created");
            sb.AppendLine($"handle: {eventHandle}");
            sb.AppendLine($"gramps_id: {eventGrampsId}");
            sb.AppendLine($"event_type: {eventType}");
            sb.AppendLine($"attached_to: {person.Handle} ({personName})");
            sb.AppendLine("---");
            if (dateStr != "\u2014")
                sb.AppendLine($"Date: {dateStr}");
            if (placeStr != "\u2014")
                sb.AppendLine($"Place: {placeStr}");
            if (role != "Primary")
                sb.AppendLine($"Role: {role}");
            if (createdObjects.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Related objects created:");
                foreach (var obj in createdObjects)
                    sb.AppendLine($"  • {obj}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex, createdObjects);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — place resolution & event creation
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record PlaceResult(string Handle, string? GrampsId, string? Name, bool Existing);

    private static async Task<PlaceResult?> ResolveOrCreatePlaceAsync(string? placeName, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(placeName))
            return null;

        var trimmed = placeName.Trim();

        // A failed lookup propagates: creating the place anyway could duplicate an existing one.
        var existing = await FindPlaceByNameAsync(trimmed, client);
        if (existing != null)
            return existing;

        // Create new place
        var request = new CreatePlaceRequest
        {
            Name = new PlaceNameRequest { Value = trimmed }
        };

        var (placeHandle, placeGrampsId) = await client.PostMutationAsync("/api/places/", request, "Place");
        return new PlaceResult(placeHandle!, placeGrampsId, trimmed, Existing: false);
    }

    /// <summary>
    /// Finds the place whose primary name equals <paramref name="name"/>, ignoring case and surrounding
    /// spaces. Gramps QL narrows the list on the server with a case-insensitive substring match, and the
    /// exact match is checked here. A name Gramps QL cannot quote (double quote, backslash, line break)
    /// is matched against every place.
    /// </summary>
    private static async Task<PlaceResult?> FindPlaceByNameAsync(string name, GrampsApiClient client)
    {
        var filter = name.IndexOfAny(['"', '\\', '\n', '\r']) < 0
            ? $"gql={Uri.EscapeDataString($"name.value ~ \"{name}\"")}&"
            : "";
        var results = await client.GetAsync<JsonElement>($"/api/places/?{filter}keys=handle,gramps_id,name");

        var items = results.ValueKind == JsonValueKind.Array
            ? results
            : results.TryGetProperty("objects", out var objArr) ? objArr : results;
        if (items.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var item in items.EnumerateArray())
        {
            var itemName = ExtractPlaceName(item);
            if (itemName != null &&
                string.Equals(itemName.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                var h = item.TryGetProperty("handle", out var hp) ? hp.GetString() : null;
                var gid = item.TryGetProperty("gramps_id", out var gp) ? gp.GetString() : null;
                if (!string.IsNullOrEmpty(h))
                    return new PlaceResult(h, gid, itemName.Trim(), Existing: true);
            }
        }

        return null;
    }

    private static string? ExtractPlaceName(JsonElement item)
    {
        if (!item.TryGetProperty("name", out var nameProp))
            return null;

        if (nameProp.ValueKind == JsonValueKind.String)
            return nameProp.GetString();

        // name can be an object with "value" key
        if (nameProp.ValueKind == JsonValueKind.Object &&
            nameProp.TryGetProperty("value", out var valueProp) &&
            valueProp.ValueKind == JsonValueKind.String)
            return valueProp.GetString();

        return null;
    }

    private static async Task<(string? Handle, string? GrampsId)> CreateEventAsync(
        string eventType, string? dateText, string? placeHandle, GrampsApiClient client)
    {
        var dateRequest = AgentDateParser.ToDateRequestOrNull(dateText, DateComponentOrder.Iso, DateIntervalPreference.Range);
        var request = new CreateEventRequest
        {
            Type = eventType,
            Date = dateRequest,
            Place = placeHandle
        };

        return await client.PostMutationAsync("/api/events/", request, "Event");
    }

    private static string FormatVitalLine(
        string? dateText, PlaceResult? placeResult, (string? Handle, string? GrampsId)? evt)
    {
        if (dateText == null && placeResult == null && evt == null)
            return "\u2014";

        var parts = new List<string>();
        if (dateText != null)
            parts.Add(dateText);

        if (placeResult != null)
            parts.Add(placeResult.Name ?? "Unknown place");

        var line = parts.Count > 0 ? string.Join(", ", parts) : "\u2014";

        if (evt?.Handle != null)
            line += $" [event: {evt.Value.Handle}]";

        return line;
    }

    private static string FormatPlaceCreationNote(PlaceResult place)
    {
        var status = place.Existing ? "existing" : "created";
        return $"Place: {place.Name ?? "Unknown"} \u2014 {place.GrampsId} (handle: {place.Handle}) [{status}]";
    }

}
