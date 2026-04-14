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
    // FindByGrampsId
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Read-only: find any Gramps object by its Gramps ID (e.g. I0001, F0023, E0005). " +
        "Automatically detects the object type from the ID prefix and returns full details. " +
        "Equivalent to calling the appropriate get_* tool but without needing to know the handle.")]
    public static async Task<string> FindByGrampsId(
        [Description(
            "Gramps ID (e.g. I0001 for person, F0023 for family, E0005 for event, " +
            "P0001 for place, S0001 for source, C0001 for citation, R0001 for repository, " +
            "N0001 for note, M0001 for media, T0001 for tag)")]
        string grampsId,
        GrampsApiClient client = null!)
    {
        try
        {
            if (!HandleResolver.LooksLikeGrampsId(grampsId))
                return "Expected a Gramps ID (e.g. I0001). For handles, use the specific get_* tool.";

            var objectType = HandleResolver.PrefixToObjectType(grampsId[0]);
            if (objectType is null)
                return $"Unknown Gramps ID prefix '{grampsId[0]}'. " +
                       "Recognized prefixes: I (person), F (family), E (event), P (place), " +
                       "S (source), C (citation), R (repository), N (note), M (media), T (tag).";

            var handle = await HandleResolver.ResolveToHandleAsync(grampsId, client);
            if (handle == grampsId)
                return NotFoundHelper.NotFoundMessage("Object", grampsId);

            return objectType switch
            {
                "people" => await FetchAndFormatPerson(handle, client),
                "families" => await FetchAndFormatFamily(handle, client),
                "events" => await FetchAndFormatEvent(handle, client),
                "places" => await FetchAndFormatPlace(handle, client),
                "sources" => await FetchAndFormatSource(handle, client),
                "citations" => await FetchAndFormatCitation(handle, client),
                "repositories" => await FetchAndFormatRepository(handle, client),
                "notes" => await FetchAndFormatNote(handle, client),
                "media" => await FetchAndFormatMedia(handle, client),
                "tags" => await FetchAndFormatTag(handle, client),
                _ => $"Unsupported object type: {objectType}"
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickAddPerson
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Convenience: create a person with optional birth and death events in a single call. " +
        "Automatically creates place and event objects as needed, then links them to the new person. " +
        "For full control over all person fields, use create_person instead.")]
    public static async Task<string> QuickAddPerson(
        [Description("Person's name as 'Given Surname' or 'Given|Surname' (e.g. 'John Smith', 'Maria|García')")]
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
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var parsedName = FlexibleGrampsNameParsing.ParseSimpleLine(name);
            var genderCode = GrampsGenderParser.ParseRequired(gender);
            var summary = new StringBuilder();
            var createdObjects = new List<string>();

            // Resolve or create places
            var birthPlaceResult = await ResolveOrCreatePlaceAsync(birthPlace, client);
            var deathPlaceResult = await ResolveOrCreatePlaceAsync(deathPlace, client);

            if (birthPlaceResult != null)
                createdObjects.Add(FormatPlaceCreationNote(birthPlaceResult));
            if (deathPlaceResult != null)
                createdObjects.Add(FormatPlaceCreationNote(deathPlaceResult));

            // Create events
            var eventHandles = new List<string>();

            GrampsEvent? birthEvent = null;
            if (birthDate != null || birthPlaceResult != null)
            {
                birthEvent = await CreateEventAsync(
                    "Birth", birthDate, birthPlaceResult?.Handle, client);
                eventHandles.Add(birthEvent.Handle!);
                createdObjects.Add($"Event (Birth): {birthEvent.GrampsId} (handle: {birthEvent.Handle})");
            }

            GrampsEvent? deathEvent = null;
            if (deathDate != null || deathPlaceResult != null)
            {
                deathEvent = await CreateEventAsync(
                    "Death", deathDate, deathPlaceResult?.Handle, client);
                eventHandles.Add(deathEvent.Handle!);
                createdObjects.Add($"Event (Death): {deathEvent.GrampsId} (handle: {deathEvent.Handle})");
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

            var person = await client.PostMutationAsync<GrampsPerson>("/api/people/", personRequest, "Person");
            createdObjects.Insert(0, $"Person: {person.GrampsId} (handle: {person.Handle})");

            // Build output
            var displayName = GrampsValueFormatter.FormatName(parsedName);
            var genderLabel = genderCode switch { 0 => "Female", 1 => "Male", _ => "Unknown" };

            summary.AppendLine($"Person created: {displayName}");
            summary.AppendLine($"  Handle: {person.Handle}, Gramps ID: {person.GrampsId}, Gender: {genderLabel}");

            var birthLine = FormatVitalLine(birthDate, birthPlaceResult, birthEvent);
            summary.AppendLine($"  Birth: {birthLine}");

            var deathLine = FormatVitalLine(deathDate, deathPlaceResult, deathEvent);
            summary.AppendLine($"  Death: {deathLine}");

            summary.AppendLine();
            summary.AppendLine("Objects created:");
            foreach (var obj in createdObjects)
                summary.AppendLine($"  \u2022 {obj}");

            return summary.ToString();
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AddEventToPerson
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool]
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
        try
        {
            if (string.IsNullOrWhiteSpace(eventType))
                throw McpToolErrors.ValidationError("Error: eventType is required. See gramps://types for valid values.");

            // Resolve person handle
            var resolvedPersonHandle = await HandleResolver.ResolveToHandleAsync(personHandle, client);

            // Fetch existing person
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>(
                $"/api/people/{Uri.EscapeDataString(resolvedPersonHandle)}");
            if (person is null)
                return NotFoundHelper.NotFoundMessage("Person", personHandle);

            var createdObjects = new List<string>();

            // Resolve or create place
            PlaceResult? placeResult = null;
            if (!string.IsNullOrWhiteSpace(place))
            {
                if (HandleResolver.LooksLikeGrampsId(place))
                {
                    var placeHandle = await HandleResolver.ResolveToHandleAsync(place, client);
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
            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso);
            var eventRequest = new CreateEventRequest
            {
                Type = eventType,
                Date = dateRequest,
                Place = placeResult?.Handle,
                Description = description
            };

            var newEvent = await client.PostMutationAsync<GrampsEvent>("/api/events/", eventRequest, "Event");
            createdObjects.Add($"Event ({eventType}): {newEvent.GrampsId} (handle: {newEvent.Handle})");

            // Build updated event ref list by appending new event
            var existingRefs = GrampsRequestMapping.ToEventRefRequests(person.EventRefList)
                               ?? Array.Empty<EventRefRequest>();
            var updatedRefs = existingRefs.Append(new EventRefRequest
            {
                Ref = newEvent.Handle,
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
                PrimaryName = person.PrimaryName != null ? ConvertNameToRequest(person.PrimaryName) : null,
                AlternateNames = person.AlternateNames?.Select(an => ConvertNameToRequest(an)).ToArray(),
                EventRefList = updatedRefs,
                FamilyList = person.FamilyList,
                ParentFamilyList = person.ParentFamilyList?.Select(pf => new FamilyRefRequest
                {
                    Ref = pf.Ref,
                    Relationship = pf.Relationship,
                    FatherRelationship = pf.FatherRelationship,
                    MotherRelationship = pf.MotherRelationship
                }).ToArray(),
                MediaList = person.MediaList,
                AddressList = person.AddressList,
                AttributeList = GrampsRequestMapping.ToAttributeRequests(person.AttributeList),
                CitationList = person.CitationList,
                NoteList = person.NoteList,
                TagList = person.TagList,
                UrlList = person.UrlList,
                PersonRefList = person.PersonRefList,
                Private = person.Private
            };

            await client.PutMutationAsync<GrampsPerson>(
                $"/api/people/{Uri.EscapeDataString(person.Handle!)}", updateRequest, "Person");

            // Build output
            var personName = person.PrimaryName != null
                ? GrampsValueFormatter.FormatName(person.PrimaryName)
                : "Unknown";
            var dateStr = newEvent.Date != null ? GrampsValueFormatter.FormatDate(newEvent.Date) : "\u2014";
            var placeStr = placeResult?.Name ?? "\u2014";

            var sb = new StringBuilder();
            sb.AppendLine($"Event added to person: {personName} [{person.GrampsId}]");
            sb.AppendLine($"  Event type: {eventType}");
            sb.AppendLine($"  Date: {dateStr}");
            sb.AppendLine($"  Place: {placeStr}");
            sb.AppendLine($"  Role: {role}");
            sb.AppendLine($"  Event handle: {newEvent.Handle}, Gramps ID: {newEvent.GrampsId}");
            sb.AppendLine();
            sb.AppendLine("Objects created:");
            foreach (var obj in createdObjects)
                sb.AppendLine($"  \u2022 {obj}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — FindByGrampsId formatters
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<string> FetchAndFormatPerson(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Person", handle) : await PersonFormatter.FormatPersonFull(obj, client);
    }

    private static async Task<string> FetchAndFormatFamily(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Family", handle) : await FamilyFormatter.FormatFamilyFullAsync(obj, client);
    }

    private static async Task<string> FetchAndFormatEvent(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsEvent>($"/api/events/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Event", handle) : await EventFormatter.FormatEventFull(obj, client);
    }

    private static async Task<string> FetchAndFormatPlace(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsPlace>($"/api/places/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Place", handle) : await PlaceFormatter.FormatPlaceFull(obj, client);
    }

    private static async Task<string> FetchAndFormatSource(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsSource>($"/api/sources/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Source", handle) : SourceFormatter.FormatSourceFull(obj);
    }

    private static async Task<string> FetchAndFormatCitation(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsCitation>($"/api/citations/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Citation", handle) : await CitationFormatter.FormatCitationFull(obj, client);
    }

    private static async Task<string> FetchAndFormatRepository(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsRepository>($"/api/repositories/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Repository", handle) : await RepositoryFormatter.FormatRepositoryFullAsync(obj, client);
    }

    private static async Task<string> FetchAndFormatNote(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsNote>($"/api/notes/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Note", handle) : await NoteFormatter.FormatNoteFullAsync(obj, client);
    }

    private static async Task<string> FetchAndFormatMedia(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Media", handle) : MediaFormatter.FormatMediaFull(obj);
    }

    private static async Task<string> FetchAndFormatTag(string handle, GrampsApiClient client)
    {
        var obj = await client.GetOrNullIfNotFoundAsync<GrampsTag>($"/api/tags/{Uri.EscapeDataString(handle)}");
        return obj is null ? NotFoundHelper.NotFoundMessage("Tag", handle) : TagFormatter.FormatTagFull(obj);
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

        var newPlace = await client.PostMutationAsync<GrampsPlace>("/api/places/", request, "Place");
        return new PlaceResult(newPlace.Handle!, newPlace.GrampsId, newPlace.Name ?? trimmed, Existing: false);
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

    private static async Task<GrampsEvent> CreateEventAsync(
        string eventType, string? dateText, string? placeHandle, GrampsApiClient client)
    {
        var dateRequest = AgentDateParser.ToDateRequestOrNull(dateText, DateComponentOrder.Iso);
        var request = new CreateEventRequest
        {
            Type = eventType,
            Date = dateRequest,
            Place = placeHandle
        };

        return await client.PostMutationAsync<GrampsEvent>("/api/events/", request, "Event");
    }

    private static string FormatVitalLine(
        string? dateText, PlaceResult? placeResult, GrampsEvent? evt)
    {
        if (dateText == null && placeResult == null && evt == null)
            return "\u2014";

        var parts = new List<string>();
        if (evt?.Date != null)
            parts.Add(GrampsValueFormatter.FormatDate(evt.Date));
        else if (dateText != null)
            parts.Add(dateText);

        if (placeResult != null)
            parts.Add(placeResult.Name ?? "Unknown place");

        var line = parts.Count > 0 ? string.Join(", ", parts) : "\u2014";

        if (evt != null)
            line += $" [event: {evt.Handle}]";

        return line;
    }

    private static string FormatPlaceCreationNote(PlaceResult place)
    {
        var status = place.Existing ? "existing" : "created";
        return $"Place: {place.Name ?? "Unknown"} \u2014 {place.GrampsId} (handle: {place.Handle}) [{status}]";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — name conversion (mirrors PersonTools.ConvertNameToRequest)
    // ─────────────────────────────────────────────────────────────────────────

    private static GrampsNameRequest ConvertNameToRequest(GrampsName name)
    {
        return new GrampsNameRequest
        {
            Call = name.Call,
            CitationList = name.CitationList,
            Date = GrampsRequestMapping.ToDateRequestOrNull(name.Date),
            DisplayAs = name.DisplayAs,
            FamNick = name.FamNick,
            FirstName = name.FirstName,
            GroupAs = name.GroupAs,
            Nick = name.Nick,
            NoteList = name.NoteList,
            Private = name.Private,
            SortAs = name.SortAs,
            Suffix = name.Suffix,
            SurnameList = name.SurnameList?.Select(s => new SurnameRequest
            {
                Surname = s.Surname,
                Prefix = s.Prefix,
                Connector = s.Connector,
                OriginType = s.OriginType,
                Primary = s.Primary
            }).ToArray(),
            Title = name.Title,
            Type = name.Type
        };
    }
}
