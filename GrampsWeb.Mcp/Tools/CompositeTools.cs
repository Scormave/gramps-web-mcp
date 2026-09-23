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
        "Convenience: create a person with optional birth and death events in a single call. " +
        "Automatically creates place and event objects as needed, then links them to the new person. " +
        "For full control over all person fields, use create_person instead.")]
    public static async Task<string> QuickAddPerson(
        [Description("Person's name as 'Given Surname' or 'Given|Surname' (e.g. 'John Smith', 'Maria|Garcia')")]
        string name,
        [Description("Gender: Female, Male, or Unknown (default: Unknown)")]
        string gender = "Unknown",
        [Description("Birth date as text (e.g. '1985-04-12', 'about 1920', 'before 1900'). Optional.")]
        string? birthDate = null,
        [Description("Birth place name (e.g. 'Moscow', 'New York, USA'). Will search for existing place or create new. Optional.")]
        string? birthPlace = null,
        [Description("Death date as text. Optional.")]
        string? deathDate = null,
        [Description("Death place name. Optional.")]
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

            var nextSteps = ResponseEnvelope.PersonCreateNextSteps(personHandle!);
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
        "Convenience: create an event and attach it to an existing person in one call. " +
        "Handles event creation + person update automatically. " +
        "For full control, use create_event + update_person separately.")]
    public static async Task<string> AddEventToPerson(
        [Description("Person handle or Gramps ID (e.g. I0001). " + ToolDescriptionFragments.HandleDiscovery)]
        string personHandle,
        [Description("Event type (e.g. 'Birth', 'Death', 'Baptism', 'Marriage', 'Burial', 'Immigration'). " +
                     "See gramps://types for all options.")]
        string eventType,
        [Description("Event date as text (e.g. '1985-04-12', 'about 1920'). Optional.")]
        string? date = null,
        [Description("Place name or handle. If a name, searches for existing place or creates new. Optional.")]
        string? place = null,
        [Description("Event description text. Optional.")]
        string? description = null,
        [Description("Person's role in this event (default: 'Primary'). Other options: Witness, Celebrant, etc.")]
        string role = "Primary",
        GrampsApiClient client = null!)
    {
        var createdObjects = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

            // Resolve person handle
            var resolvedPersonHandle = await HandleResolver.ResolveToHandleAsync(personHandle, client, "people");

            // Fetch existing person
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>(
                $"/api/people/{Uri.EscapeDataString(resolvedPersonHandle)}");
            if (person is null)
                return NotFoundHelper.NotFoundMessage("Person", personHandle);

            // Resolve or create place
            PlaceResult? placeResult = null;
            if (!string.IsNullOrWhiteSpace(place))
            {
                if (HandleResolver.LooksLikeGrampsId(place))
                {
                    var placeHandle = await HandleResolver.ResolveToHandleAsync(place, client, "places");
                    var existingPlace = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                        $"/api/places/{Uri.EscapeDataString(placeHandle)}");
                    if (existingPlace != null)
                        placeResult = new PlaceResult(existingPlace.Handle!, existingPlace.GrampsId, existingPlace.Name, true);
                }
                else if (place.Length > 10)
                {
                    // Looks like a handle (long string), try to use directly
                    var existingPlace = await client.GetOrNullIfNotFoundAsync<GrampsPlace>(
                        $"/api/places/{Uri.EscapeDataString(place)}");
                    if (existingPlace != null)
                        placeResult = new PlaceResult(existingPlace.Handle!, existingPlace.GrampsId, existingPlace.Name, true);
                }

                placeResult ??= await ResolveOrCreatePlaceAsync(place, client);

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

        // Search for existing place by name
        try
        {
            var searchPath = $"/api/places/?pagesize=5&keys=handle,gramps_id,name";
            var results = await client.GetAsync<JsonElement>(searchPath);

            var items = results.ValueKind == JsonValueKind.Array
                ? results
                : results.TryGetProperty("objects", out var objArr) ? objArr : results;

            if (items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var itemName = ExtractPlaceName(item);
                    if (itemName != null &&
                        string.Equals(itemName.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        var h = item.TryGetProperty("handle", out var hp) ? hp.GetString() : null;
                        var gid = item.TryGetProperty("gramps_id", out var gp) ? gp.GetString() : null;
                        if (!string.IsNullOrEmpty(h))
                            return new PlaceResult(h, gid, itemName.Trim(), Existing: true);
                    }
                }
            }
        }
        catch
        {
            // Fall through to create
        }

        // Create new place
        var request = new CreatePlaceRequest
        {
            Name = new PlaceNameRequest { Value = trimmed }
        };

        var (placeHandle, placeGrampsId) = await client.PostMutationAsync("/api/places/", request, "Place");
        return new PlaceResult(placeHandle!, placeGrampsId, trimmed, Existing: false);
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
