using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools.Parsing;
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
        "Read-only: fetch one person by handle. With extended=true, resolves linked objects " +
        "(event dates/places, note text, tag names, citations, media) for a fuller picture in one call. " +
        "Default extended=false returns core fields with handles only (faster).")]
    public static async Task<string> GetPerson(
        [Description("Person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("When true, resolve linked events/notes/tags/citations/media inline. Slower but more complete. Default: false.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (extended)
            {
                var person = await client.GetOrNullIfNotFoundAsync<GrampsPersonExtended>($"/api/people/{handle}?extend=all");
                if (person == null)
                    return NotFoundHelper.NotFoundMessage("Person", handle);
                await ExtendedEntityEnrichment.EnrichPersonExtendedAsync(person, client);
                return await PersonFormatter.FormatPersonExtended(person, client);
            }
            else
            {
                var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle}");
                return person == null
                    ? NotFoundHelper.NotFoundMessage("Person", handle)
                    : await PersonFormatter.FormatPersonFull(person, client);
            }
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Read-only: list ancestors up to N generations with names and vital dates/places. " +
        "Each row includes generation and optional kinship labels (Father, Mother's father, …). " +
        "Only ancestors via parent families appear; spouse-only links do not.")]
    public static async Task<string> GetAncestors(
        [Description("Root person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Number of ancestor generations to include (default: 3, max: 10)")]
        int generations = 3,
        [Description("When true (default), add kinship text from the father/mother chain (e.g. Father's mother). When false, only Gen N.")]
        bool kinshipLabels = true,
        GrampsApiClient client = null!)
    {
        try
        {
            generations = Math.Clamp(generations, 1, 10);
            var ancestors = await PersonTreeTraversal.CollectAncestorsAsync(client, handle, generations);
            if (ancestors == null)
                return NotFoundHelper.NotFoundMessage("Person", handle);
            if (ancestors.Length == 0)
                return
                    $"No ancestors found for {handle}. " +
                    "Only people linked through a parent family (where this person is the child) appear. " +
                    "Spouse-only links do not count as ancestors. Use get_person_extended to inspect family links.";
            return await PersonFormatter.FormatPersonTreeRows("ANCESTOR TREE", handle, ancestors, kinshipLabels, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Read-only: list descendants up to N generations with names and vital dates/places. " +
        "Each row includes generation and optional kinship (Son, Granddaughter, …) from recorded gender. " +
        "Only children on families where this person is a parent are included.")]
    public static async Task<string> GetDescendants(
        [Description("Root person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Number of descendant generations to include (default: 3, max: 10)")]
        int generations = 3,
        [Description("When true (default), add kinship (Son/Daughter/Grandson/…). When false, only Gen N.")]
        bool kinshipLabels = true,
        GrampsApiClient client = null!)
    {
        try
        {
            generations = Math.Clamp(generations, 1, 10);
            var descendants = await PersonTreeTraversal.CollectDescendantsAsync(client, handle, generations);
            if (descendants == null)
                return NotFoundHelper.NotFoundMessage("Person", handle);
            if (descendants.Length == 0)
                return
                    $"No descendants found for {handle}. " +
                    "Only children linked on families where this person is a parent are included; if none are recorded, the list is empty.";
            return await PersonFormatter.FormatPersonTreeRows("DESCENDANT TREE", handle, descendants, kinshipLabels, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Read-only: chronological timeline of events for one person (and optionally relatives' events). " +
        "Filter with events (categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom), " +
        "relatives (father, mother, brother, sister, wife, husband, son, daughter), and relative_events (same categories). " +
        "dates: range YYYY/M/D-YYYY/M/D or open-ended; month/day leading zeros are stripped for the API. " +
        "Output may include event handles for follow-up with get_event.")]
    public static async Task<string> GetPersonTimeline(
        [Description("Person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Event categories to include: vital, family, religious, vocational, academic, travel, legal, residence, other, custom")]
        string[]? events = null,
        [Description("Include events of relatives: father, mother, brother, sister, wife, husband, son, daughter")]
        string[]? relatives = null,
        [Description("Event categories for the listed relatives (same options as events)")]
        string[]? relativeEvents = null,
        [Description("Date range filter; e.g. 1999/1/1-2010/12/31 or 1999/01/01-2010/01/01 (zeros stripped for API)")]
        string? dates = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var qs = BuildTimelineQueryString(events, relatives, relativeEvents, dates, true);
            var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
                $"/api/people/{handle}/timeline{qs}");
            if (timeline == null)
                return NotFoundHelper.NotFoundMessage("Person", handle);
            if (timeline.Length == 0)
                return $"No timeline events found for {handle}. " +
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
        "Read-only: genealogical relationship between two people (e.g. '3rd cousin twice removed'), " +
        "path distance, and common-ancestor handles, or a clear message if unrelated.")]
    public static async Task<string> GetRelations(
        [Description("First person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle1,
        [Description("Second person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle2,
        GrampsApiClient client)
    {
        try
        {
            var result = await client.GetJsonOrNullIfNotFoundAsync(
                $"/api/relations/{handle1}/{handle2}");
            if (result is null)
            {
                if (await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle1}") is null)
                    return NotFoundHelper.NotFoundMessage("Person", handle1);
                if (await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle2}") is null)
                    return NotFoundHelper.NotFoundMessage("Person", handle2);
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
        "Create a new person (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetNameSchema + " " + ToolDescriptionFragments.CallGetTypes + " " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide + " " +
        "Link events in one call with eventRefs, including per-link role metadata.")]
    public static async Task<string> CreatePerson(
        [Description(FlexibleGrampsName.DescriptionHint)]
        FlexibleGrampsName? primaryName,
        [Description("Gender: Female, Male, or Unknown (default Unknown).")]
        string gender = "Unknown",
        [Description(FlexibleAlternateNameList.DescriptionHint)]
        FlexibleAlternateNameList? alternateNames = null,
        [Description("Event links to attach to this person. " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Family handles (where this person is parent/spouse). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? familyHandles = null,
        [Description("Parent family handles (where this person is child). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? parentFamilyHandles = null,
        [Description("Media object handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description(FlexibleAddressList.DescriptionHint)]
        FlexibleAddressList? addresses = null,
        [Description(FlexibleUrlList.DescriptionHint)]
        FlexibleUrlList? urls = null,
        [Description(FlexiblePersonRefList.DescriptionHint)]
        FlexiblePersonRefList? personAssociations = null,
        [Description("Mark record as private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (primaryName?.Name == null)
                throw McpToolErrors.ValidationError("Error: primaryName is required");

            var primary = primaryName.Name;
            var genderCode = GrampsGenderParser.ParseRequired(gender);

            var eventRefArr = (EventRefRequest[]?)eventRefs ?? [];

            var parentFamilyHandleArray = (string[]?)parentFamilyHandles;

            var request = new CreatePersonRequest
            {
                Gender = genderCode,
                PrimaryName = ConvertNameToRequest(primary),
                AlternateNames = (GrampsName[]?)alternateNames is { Length: > 0 } alts
                    ? alts.Select(ConvertNameToRequest).ToArray()
                    : null,
                EventRefList = eventRefArr.Length > 0 ? eventRefArr : null,
                FamilyList = familyHandles,
                ParentFamilyList = parentFamilyHandleArray?.Length > 0 ? parentFamilyHandleArray : null,
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                AddressList = (GrampsAddress[]?)addresses is { Length: > 0 } a ? a : null,
                UrlList = (GrampsUrl[]?)urls is { Length: > 0 } u ? u : null,
                PersonRefList = (GrampsPersonRef[]?)personAssociations is { Length: > 0 } p ? p : null,
                Private = isPrivate
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/people/", request, "Person");
            return ResponseEnvelope.CreateSuccess(
                "Person", handle, grampsId,
                GrampsValueFormatter.FormatName(primary),
                ResponseEnvelope.PersonCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update an existing person (write). Only include arguments you want to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        "Replacing eventRefs replaces the full event list. " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdatePerson(
        [Description("Person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Replace primary name. " + ToolDescriptionFragments.OmitToKeepScalar + " " + FlexibleGrampsName.DescriptionHint)]
        FlexibleGrampsName? primaryName = null,
        [Description("Gender: Female, Male, or Unknown. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? gender = null,
        [Description("Replace all alternate names. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAlternateNameList.DescriptionHint)]
        FlexibleAlternateNameList? alternateNames = null,
        [Description("Replace all person–event links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Replace families where this person is parent/spouse. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? familyHandles = null,
        [Description("Replace parent (child-of) families. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? parentFamilyHandles = null,
        [Description("Replace media links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace citation links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace note links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tag links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Replace addresses. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAddressList.DescriptionHint)]
        FlexibleAddressList? addresses = null,
        [Description("Replace URLs. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleUrlList.DescriptionHint)]
        FlexibleUrlList? urls = null,
        [Description("Replace person associations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePersonRefList.DescriptionHint)]
        FlexiblePersonRefList? personAssociations = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            // Get current person first
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle}");
            if (person == null)
                return NotFoundHelper.NotFoundMessage("Person", handle);

            var primaryReq = primaryName?.Name != null
                ? ConvertNameToRequest(primaryName.Name)
                : person.PrimaryName != null ? ConvertNameToRequest(person.PrimaryName) : null;

            // Build update request with provided fields or existing values
            var updateRequest = new CreatePersonRequest
            {
                Class = "Person",
                Handle = person.Handle,
                GrampsId = person.GrampsId,
                Change = person.Change,
                Gender = GrampsGenderParser.ParseOptional(gender) ?? person.Gender,
                PrimaryName = primaryReq,
                AlternateNames = alternateNames != null
                    ? ((GrampsName[]?)alternateNames)!.Select(ConvertNameToRequest).ToArray()
                    : person.AlternateNames?.Select(ConvertNameToRequest).ToArray(),
                EventRefList = eventRefs != null
                    ? (EventRefRequest[]?)eventRefs
                    : GrampsRequestMapping.ToEventRefRequests(person.EventRefList),
                FamilyList = (string[]?)familyHandles ?? person.FamilyList,
                ParentFamilyList = (string[]?)parentFamilyHandles
                    ?? GrampsRequestMapping.ToParentFamilyHandles(person.ParentFamilyList),
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles ?? person.MediaList),
                AddressList = addresses is null ? person.AddressList : (GrampsAddress[]?)addresses,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(person.AttributeList),
                CitationList = (string[]?)citationHandles ?? person.CitationList,
                NoteList = (string[]?)noteHandles ?? person.NoteList,
                TagList = (string[]?)tagHandles ?? person.TagList,
                UrlList = urls is null ? person.UrlList : (GrampsUrl[]?)urls,
                PersonRefList = personAssociations is null ? person.PersonRefList : (GrampsPersonRef[]?)personAssociations,
                Private = isPrivate ?? person.Private
            };

            await client.PutMutationAsync($"/api/people/{handle}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Person", person.Handle, person.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    internal static string BuildTimelineQueryString(
        string[]? events, string[]? relatives, string[]? relativeEvents,
        string? dates, bool includeUndated = true)
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

    internal static GrampsNameRequest ConvertNameToRequest(GrampsName name)
    {
        var dateReq = GrampsRequestMapping.ToDateRequestOrNull(name.Date);

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

    [McpServerTool]
    [Description(
        "Delete a person (destructive). Blocked when backlinks exist unless force=true. " +
        "WARNING: force=true can leave dangling references in family and event records.")]
    public static async Task<string> DeletePerson(
        [Description("Person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete even when other objects still reference this person (dangerous; default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Person", "people", handle, force);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
