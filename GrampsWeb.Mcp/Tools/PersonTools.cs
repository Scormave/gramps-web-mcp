using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Person objects from the Gramps Web API.
/// Covers get_person, get_person_extended, get_ancestors, get_descendants,
/// get_person_timeline, and get_relations.
/// </summary>
[McpServerToolType]
public static class PersonTools
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tools
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Get raw person data by handle. Returns name, gender, gramps_id, birth/death dates and places, " +
        "handles of linked events, families, notes and media. " +
        "Use get_person_extended for all referenced objects resolved in one call. " +
        "Use get_ancestors or get_descendants for family tree traversal. " +
        "Always call get_name_schema before working with name fields.")]
    public static async Task<string> GetPerson(
        [Description("Person handle — use search() or list_objects('people') to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle}");
            return person == null
                ? $"Person not found: {handle}"
                : await PersonFormatter.FormatPersonFull(person, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get complete person data with all referenced objects resolved in a single request. " +
        "Returns resolved event dates, place names, family handles, note text, and tag names. " +
        "Use for 'tell me everything about this person' queries. Slower than get_person.")]
    public static async Task<string> GetPersonExtended(
        [Description("Person handle")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPersonExtended>($"/api/people/{handle}?extend=all");
            return person == null
                ? $"Person not found: {handle}"
                : await PersonFormatter.FormatPersonExtended(person, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get ancestor tree up to N generations. Returns names, birth/death dates and places " +
        "for each ancestor. Uses server-side BFS traversal — no HTML reports. " +
        "Use handle from search() or list_objects('people').")]
    public static async Task<string> GetAncestors(
        [Description("Person handle")]
        string handle,
        [Description("Number of ancestor generations to include (default: 3, max: 10)")]
        int generations = 3,
        GrampsApiClient client = null!)
    {
        try
        {
            generations = Math.Clamp(generations, 1, 10);
            var ancestors = await client.GetOrNullIfNotFoundAsync<GrampsPerson[]>(
                $"/api/people/{handle}/ancestors?generations={generations}");
            if (ancestors == null)
                return $"Person not found: {handle}";
            if (ancestors.Length == 0)
                return $"No ancestors found for {handle}";
            return await PersonFormatter.FormatPersonList("ANCESTOR TREE", handle, ancestors, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get descendant tree up to N generations. Returns names, birth/death dates and places " +
        "for each descendant. Uses server-side BFS traversal.")]
    public static async Task<string> GetDescendants(
        [Description("Person handle")]
        string handle,
        [Description("Number of descendant generations to include (default: 3, max: 10)")]
        int generations = 3,
        GrampsApiClient client = null!)
    {
        try
        {
            generations = Math.Clamp(generations, 1, 10);
            var descendants = await client.GetOrNullIfNotFoundAsync<GrampsPerson[]>(
                $"/api/people/{handle}/descendants?generations={generations}");
            if (descendants == null)
                return $"Person not found: {handle}";
            if (descendants.Length == 0)
                return $"No descendants found for {handle}";
            return await PersonFormatter.FormatPersonList("DESCENDANT TREE", handle, descendants, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get chronological timeline of life events for a person. " +
        "events: filter by category — vital, family, religious, vocational, academic, travel, legal, residence, other, custom. " +
        "relatives: include events of — father, mother, brother, sister, wife, husband, son, daughter. " +
        "relative_events: event categories for relatives (same options as events). " +
        "dates: range 'YYYY/MM/DD-YYYY/MM/DD', or open 'YYYY/MM/DD-' or '-YYYY/MM/DD'. Leading zeros in month/day are normalized for the API. " +
        "include_undated: default true — Gramps may still show a formatted date while sortval is 0; the API hides those unless discard_empty=false. " +
        "Set false only to match strict API default (omit events Gramps treats as undated). " +
        "ratings: include citation confidence scores (0=very low … 4=very high).")]
    public static async Task<string> GetPersonTimeline(
        [Description("Person handle")]
        string handle,
        [Description("Event categories to include: vital, family, religious, vocational, academic, travel, legal, residence, other, custom")]
        string[]? events = null,
        [Description("Include events of relatives: father, mother, brother, sister, wife, husband, son, daughter")]
        string[]? relatives = null,
        [Description("Event categories for the listed relatives (same options as events)")]
        string[]? relativeEvents = null,
        [Description("Date range filter; e.g. 1999/1/1-2010/12/31 or 1999/01/01-2010/01/01 (zeros stripped for API)")]
        string? dates = null,
        [Description("Include events Gramps marks undated (sortval 0); default true. Use false for API-strict filtering.")]
        bool includeUndated = true,
        [Description("When true, include citation confidence rating (★) for each event")]
        bool ratings = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var qs = BuildTimelineQueryString(events, relatives, relativeEvents, dates, ratings, includeUndated);
            var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
                $"/api/people/{handle}/timeline{qs}");
            if (timeline == null)
                return $"Person not found: {handle}";
            if (timeline.Length == 0)
                return $"No timeline events found for {handle}. " +
                    "If you use include_undated=false, events with sortval 0 are hidden by the API even when a date displays in Gramps. " +
                    "Only linked events (and relatives per filters) appear; a name date alone is not a timeline event.";
            return TimelineFormatter.FormatTimelineChronological(timeline);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Calculate the genealogical relationship between two people. " +
        "Returns the relationship string (e.g. '3rd cousin twice removed'), " +
        "the path distance to common ancestors, and handles of common ancestors. " +
        "Returns 'no relationship found' when the people are unrelated.")]
    public static async Task<string> GetRelations(
        [Description("Handle of the first person")]
        string handle1,
        [Description("Handle of the second person")]
        string handle2,
        GrampsApiClient client)
    {
        try
        {
            var result = await client.GetJsonOrNullIfNotFoundAsync(
                $"/api/people/{handle1}/relationships?other={Uri.EscapeDataString(handle2)}");
            if (result is null)
            {
                if (await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle1}") is null)
                    return $"Person not found: {handle1}";
                if (await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle2}") is null)
                    return $"Person not found: {handle2}";
                return "Could not retrieve relationship data for these handles.";
            }

            return PersonFormatter.FormatRelationships(handle1, handle2, result.Value);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a new person. IMPORTANT: call get_name_schema first to understand the Name structure. " +
        "Call get_types() for valid event type values. " +
        "Pass event handles directly — no separate update call needed. " +
        "eventRefHandles and eventRefRoles must be same length (use 'Primary' for main events). " +
        "Returns the handle of the created person. " +
        "primaryNameDate overrides any date embedded in primaryName. Date formats: get_date_input_guide().")]
    public static async Task<string> CreatePerson(
        [Description("Primary name object. Must include first_name and surname_list (see get_name_schema)")]
        GrampsName primaryName,
        [Description("Primary name date as text (optional). Overrides primaryName.date. Formats: get_date_input_guide().")]
        string? primaryNameDate = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder primaryNameDateOrder = DateComponentOrder.Iso,
        [Description("Gender: 0=Female, 1=Male, 2=Unknown (default: 2)")]
        int gender = 2,
        [Description("Array of event handles to link to this person")]
        string[]? eventRefHandles = null,
        [Description("Array of event roles (e.g. 'Primary', 'Witness'). Must match eventRefHandles length")]
        string[]? eventRefRoles = null,
        [Description("Array of family handles (where this person is parent/spouse)")]
        string[]? familyHandles = null,
        [Description("Array of parent family handles (where this person is child)")]
        string[]? parentFamilyHandles = null,
        [Description("Array of note handles")]
        string[]? noteHandles = null,
        [Description("Array of tag handles")]
        string[]? tagHandles = null,
        [Description("Mark record as private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (primaryName == null)
                throw McpToolErrors.ValidationError("Error: primaryName is required");
            
            // Build event_ref_list
            var eventRefList = new List<EventRefRequest>();
            if (eventRefHandles?.Length > 0)
            {
                for (int i = 0; i < eventRefHandles.Length; i++)
                {
                    var role = eventRefRoles?.Length > i ? eventRefRoles[i] : "Primary";
                    eventRefList.Add(new EventRefRequest { Ref = eventRefHandles[i], Role = role });
                }
            }

            // Build parent family list
            var parentFamilyList = new List<FamilyRefRequest>();
            if (parentFamilyHandles?.Length > 0)
            {
                foreach (var pfHandle in parentFamilyHandles)
                {
                    parentFamilyList.Add(new FamilyRefRequest { Ref = pfHandle });
                }
            }

            var request = new CreatePersonRequest
            {
                Gender = gender,
                PrimaryName = ConvertNameToRequest(primaryName, primaryNameDate, primaryNameDateOrder),
                AlternateNames = null,
                EventRefList = eventRefList.Count > 0 ? eventRefList.ToArray() : null,
                FamilyList = familyHandles,
                ParentFamilyList = parentFamilyList.Count > 0 ? parentFamilyList.ToArray() : null,
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsPerson>("/api/people/", request, "Person");
            return $"Person created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Name: {GrampsValueFormatter.FormatName(primaryName)}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing person. Pass only fields that need to change. " +
        "To add an event, include existing event handles in eventRefHandles. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects (e.g. empty tagHandles removes all tags). " +
        "primaryNameDate updates only the name date when primaryName is omitted (requires existing primary name). " +
        "Date formats: get_date_input_guide().")]
    public static async Task<string> UpdatePerson(
        [Description("Person handle")]
        string handle,
        [Description("Update to primary name (leave null to keep unchanged)")]
        GrampsName? primaryName = null,
        [Description("Primary name date as text (optional). When primaryName is set, overrides its date; otherwise updates stored name date. Formats: get_date_input_guide().")]
        string? primaryNameDate = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder primaryNameDateOrder = DateComponentOrder.Iso,
        [Description("Update gender: 0=Female, 1=Male, 2=Unknown")]
        int? gender = null,
        [Description("Replace event references")]
        string[]? eventRefHandles = null,
        [Description("Event roles to match eventRefHandles length")]
        string[]? eventRefRoles = null,
        [Description("Replace family handles")]
        string[]? familyHandles = null,
        [Description("Replace parent family handles")]
        string[]? parentFamilyHandles = null,
        [Description("Replace note handles")]
        string[]? noteHandles = null,
        [Description("Replace tag handles")]
        string[]? tagHandles = null,
        [Description("Update private status")]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            // Get current person first
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle}");
            if (person == null)
                return $"Person not found: {handle}";

            GrampsNameRequest? primaryReq;
            if (primaryName != null)
                primaryReq = ConvertNameToRequest(primaryName, primaryNameDate, primaryNameDateOrder);
            else if (primaryNameDate != null)
            {
                if (person.PrimaryName == null)
                    throw McpToolErrors.ValidationError(
                        "primaryNameDate requires an existing primary name on the person, or pass primaryName.");
                primaryReq = ConvertNameToRequest(person.PrimaryName, primaryNameDate, primaryNameDateOrder);
            }
            else
                primaryReq = person.PrimaryName != null ? ConvertNameToRequest(person.PrimaryName) : null;

            // Build update request with provided fields or existing values
            var updateRequest = new CreatePersonRequest
            {
                Class = "Person",
                Handle = person.Handle,
                GrampsId = person.GrampsId,
                Change = person.Change,
                Gender = gender ?? person.Gender,
                PrimaryName = primaryReq,
                AlternateNames = person.AlternateNames?.Select(an => ConvertNameToRequest(an)).ToArray(),
                EventRefList = eventRefHandles != null
                    ? BuildEventRefList(eventRefHandles, eventRefRoles)
                    : GrampsRequestMapping.ToEventRefRequests(person.EventRefList),
                FamilyList = familyHandles ?? person.FamilyList,
                ParentFamilyList = parentFamilyHandles != null
                    ? parentFamilyHandles.Select(h => new FamilyRefRequest { Ref = h }).ToArray()
                    : GrampsRequestMapping.ToFamilyRefRequests(person.ParentFamilyList),
                MediaList = person.MediaList,
                AddressList = person.AddressList,
                AttributeList = GrampsRequestMapping.ToAttributeRequests(person.AttributeList),
                CitationList = person.CitationList,
                NoteList = noteHandles ?? person.NoteList,
                TagList = tagHandles ?? person.TagList,
                Private = isPrivate ?? person.Private
            };

            var response = await client.PutMutationAsync<GrampsPerson>($"/api/people/{handle}", updateRequest, "Person");
            return $"Person updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    internal static string BuildTimelineQueryString(
        string[]? events, string[]? relatives, string[]? relativeEvents,
        string? dates, bool ratings, bool includeUndated = true)
    {
        var queryParams = new List<string>();
        // Gramps Web API uses comma-delimited event_classes / relative_event_classes (not repeated events= for categories).
        if (events?.Length > 0)
            queryParams.Add($"event_classes={Uri.EscapeDataString(string.Join(",", events))}");
        if (relatives?.Length > 0)
            queryParams.Add($"relatives={Uri.EscapeDataString(string.Join(",", relatives))}");
        if (relativeEvents?.Length > 0)
            queryParams.Add($"relative_event_classes={Uri.EscapeDataString(string.Join(",", relativeEvents))}");
        var normalizedDates = NormalizeTimelineDatesForGrampsApi(dates);
        if (!string.IsNullOrEmpty(normalizedDates))
            queryParams.Add($"dates={Uri.EscapeDataString(normalizedDates)}");
        // Default true: Gramps timeline drops events when date.sortval==0 (API discard_empty default), even if a display date exists.
        if (includeUndated)
            queryParams.Add("discard_empty=false");
        if (ratings)
            queryParams.Add("ratings=1");
        return queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
    }

    /// <summary>
    /// Gramps Web timeline <c>dates</c> query is validated with a regex that disallows leading zeros
    /// in month and day (<c>1999/1/1</c> not <c>1999/01/01</c>).
    /// </summary>
    internal static string? NormalizeTimelineDatesForGrampsApi(string? dates)
    {
        if (string.IsNullOrWhiteSpace(dates))
            return dates;

        var s = dates.Trim();

        if (s.StartsWith("-", StringComparison.Ordinal))
        {
            var rest = s[1..];
            return "-" + NormalizeYmdSegment(rest);
        }

        if (s.EndsWith("-", StringComparison.Ordinal)
            && !s[..^1].Contains('-', StringComparison.Ordinal))
        {
            var rest = s[..^1];
            return NormalizeYmdSegment(rest) + "-";
        }

        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && dash < s.Length - 1)
        {
            var left = s[..dash];
            var right = s[(dash + 1)..];
            return $"{NormalizeYmdSegment(left)}-{NormalizeYmdSegment(right)}";
        }

        return NormalizeYmdSegment(s);
    }

    private static string NormalizeYmdSegment(string segment)
    {
        var parts = segment.Split('/');
        if (parts.Length != 3)
            return segment;

        if (parts[0].Contains('*', StringComparison.Ordinal)
            || parts[1].Contains('*', StringComparison.Ordinal)
            || parts[2].Contains('*', StringComparison.Ordinal))
            return segment;

        if (!int.TryParse(parts[0], out var y)
            || !int.TryParse(parts[1], out var m)
            || !int.TryParse(parts[2], out var d))
            return segment;

        return $"{y}/{m}/{d}";
    }

    private static GrampsNameRequest ConvertNameToRequest(
        GrampsName name,
        string? dateOverride = null,
        DateComponentOrder dateOrder = DateComponentOrder.Iso)
    {
        var dateReq = dateOverride != null
            ? AgentDateParser.ToDateRequestOrNull(dateOverride, dateOrder)
            : GrampsRequestMapping.ToDateRequestOrNull(name.Date);

        return new GrampsNameRequest
        {
            Call = name.Call,
            CitationList = name.CitationList,
            Date = dateReq,
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

    private static EventRefRequest[] BuildEventRefList(string[]? handles, string[]? roles)
    {
        if (handles?.Length == 0) return Array.Empty<EventRefRequest>();
        var list = new List<EventRefRequest>();
        for (int i = 0; i < handles!.Length; i++)
        {
            list.Add(new EventRefRequest
            {
                Ref = handles[i],
                Role = roles?.Length > i ? roles[i] : "Primary"
            });
        }
        return list.ToArray();
    }

    [McpServerTool]
    [Description(
        "Delete a person. Checks backlinks first — will warn if person is " +
        "referenced in families or events. Use force=true to delete despite references, " +
        "but this will leave dangling handles in Family objects.")]
    public static async Task<string> DeletePerson(
        [Description("Person handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false) — WARNING: leaves dangling references")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            // Get object with backlinks to check dependencies
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/people/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Person not found: {handle}";
            var response = payload.Value;

            // Check for backlinks
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
                            foreach (var item in property.Value.EnumerateArray().Take(3))
                            {
                                if (item.TryGetProperty("gramps_id", out var grampsId))
                                    backlinksInfo.AppendLine($"    - {grampsId.GetString()}");
                            }
                        }
                    }
                }
            }

            // If backlinks exist and force is false, return warning
            if (hasBacklinks && !force)
            {
                return $"⚠️ Cannot delete person [{handle}] — it has references:\n" +
                       $"{backlinksInfo}\n" +
                       $"To delete anyway, call delete_person(handle, force=true).\n" +
                       $"Warning: this will leave dangling references in Family objects.";
            }

            // Delete the person
            await client.DeleteAsync($"/api/people/{handle}");
            return $"Person deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
