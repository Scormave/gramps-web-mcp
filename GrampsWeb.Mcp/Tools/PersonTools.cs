using System.ComponentModel;
using System.Text;
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
/// Covers person traversal, relations, and person mutations.
/// </summary>
[McpServerToolType]
public static class PersonTools
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tools
    // ─────────────────────────────────────────────────────────────────────────

    [Description(
        "Read-only: fetch one person by handle. With extended=true, resolves linked objects " +
        "(event dates/places, note text, tag names, citations, media) for a fuller picture in one call. " +
        "Default extended=false returns core fields with handles only (faster).")]
    internal static async Task<string> ReadPersonAsync(
        [Description("Person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("When true, resolve linked events/notes/tags/citations/media inline. Slower but more complete. Default: false.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "people");
            if (extended)
            {
                var person = await client.GetOrNullIfNotFoundAsync<GrampsPersonExtended>(
                    $"/api/people/{Uri.EscapeDataString(resolvedHandle)}?extend=all&profile=families");
                if (person == null)
                    return NotFoundHelper.NotFoundMessage("Person", handle);
                await ExtendedEntityEnrichment.EnrichPersonExtendedAsync(person, client);
                return await PersonFormatter.FormatPersonExtended(person, client);
            }
            else
            {
                var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>(
                    $"/api/people/{Uri.EscapeDataString(resolvedHandle)}?profile=self");
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

    [McpServerTool(Title = "Get Person Tree", ReadOnly = true, Destructive = false)]
    [Description(
        "List a person's ancestors or descendants up to 10 generations deep: one row per relative with the generation, " +
        "an optional kinship label (Father's mother, Granddaughter), name, Gramps ID, birth and death with places, and handle. " +
        "Ancestors follow the person's parent families; descendants follow the children of families where the person is a parent, " +
        "so relatives not linked that way are missing. For how two given people are related use get_relations; " +
        "for dated events around one person use get_timeline; for one full record use get_object.")]
    public static async Task<string> GetPersonTree(
        [Description("The person to start from. " + ToolDescriptionFragments.HandleDiscovery)]
        string person,
        [Description("ancestors (parents, grandparents, …) or descendants (children, grandchildren, …).")]
        string direction,
        [Description("How many generations to walk, 1–10 (default 3); other values are clamped.")]
        int generations = 3,
        [Description("When true (default), add kinship text such as Father's mother or Granddaughter. When false, show only generation numbers.")]
        bool kinshipLabels = true,
        GrampsApiClient client = null!)
    {
        try
        {
            generations = Math.Clamp(generations, 1, 10);
            var normalizedDirection = direction.Trim().ToLowerInvariant();
            if (normalizedDirection is not ("ancestors" or "descendants"))
                throw McpToolErrors.ValidationError("Invalid direction. Must be either ancestors or descendants.");

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(person, client, "people");
            var tree = normalizedDirection == "ancestors"
                ? await PersonTreeTraversal.CollectAncestorsAsync(client, resolvedHandle, generations)
                : await PersonTreeTraversal.CollectDescendantsAsync(client, resolvedHandle, generations);
            if (tree == null)
                return NotFoundHelper.NotFoundMessage("Person", person);
            if (tree.Rows.Length == 0)
            {
                return normalizedDirection == "ancestors"
                    ? $"No ancestors found for {person}. Only people linked through a parent family (where this person is the child) appear. " +
                      "Spouse-only links do not count as ancestors. Use get_object(objectType: \"person\", extended: true) to inspect family links."
                    : $"No descendants found for {person}. Only children linked on families where this person is a parent are included; if none are recorded, the list is empty.";
            }

            var title = normalizedDirection == "ancestors" ? "ANCESTOR TREE" : "DESCENDANT TREE";
            return await PersonFormatter.FormatPersonTreeRows(title, tree, kinshipLabels, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>Common ancestors fetched by name; any beyond are listed by handle.</summary>
    internal const int MaxNamedCommonAncestors = 10;

    [McpServerTool(Title = "Get Relations", ReadOnly = true, Destructive = false)]
    [Description(
        "Explain how two people are related, read as 'person 2 is the X of person 1' " +
        "(e.g. 'third cousin twice removed', 'husband'): the closest relationship with generations to the common ancestor, " +
        "then every relationship found with its common ancestors by name. Searches blood relatives up to 15 generations " +
        "plus spouses; unrelated people get a clear message. Find both people first with search. " +
        "To list a whole line of ancestors or descendants use get_person_tree.")]
    public static async Task<string> GetRelations(
        [Description("Person 1, the one the relationship is told from. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle1,
        [Description("Person 2, the one whose relationship to person 1 is named. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle2,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle1 = await HandleResolver.ResolveToHandleAsync(handle1, client, "people");
            var resolvedHandle2 = await HandleResolver.ResolveToHandleAsync(handle2, client, "people");
            var escaped1 = Uri.EscapeDataString(resolvedHandle1);
            var escaped2 = Uri.EscapeDataString(resolvedHandle2);

            var person1Task = GetPersonProfileAsync(client, resolvedHandle1);
            if (resolvedHandle1 == resolvedHandle2)
            {
                var person = await person1Task;
                return person is null
                    ? NotFoundHelper.NotFoundMessage("Person", handle1)
                    : $"{PersonFormatter.FormatRelationPerson(person.Profile, resolvedHandle1)}: both handles refer to the same person.";
            }

            // Independent requests run together; the relationship calculations are the slow part.
            var person2Task = GetPersonProfileAsync(client, resolvedHandle2);
            var relationTask = client.GetOrNullIfNotFoundAsync<GrampsRelationship>($"/api/relations/{escaped1}/{escaped2}");
            var allTask = client.GetOrNullIfNotFoundAsync<GrampsRelationshipItem[]>($"/api/relations/{escaped1}/{escaped2}/all");
            await Task.WhenAll(person1Task, person2Task, relationTask, allTask);

            var person1 = await person1Task;
            var person2 = await person2Task;
            if (person1 is null)
                return NotFoundHelper.NotFoundMessage("Person", handle1);
            if (person2 is null)
                return NotFoundHelper.NotFoundMessage("Person", handle2);
            var relation = await relationTask;
            if (relation is null)
                return "Could not retrieve relationship data for these handles.";

            var all = await allTask ?? [];
            // A direct ancestor is its own common ancestor, so both people are already known.
            var ancestors = new Dictionary<string, GrampsPersonProfile>();
            if (person1.Profile is { } profile1)
                ancestors[resolvedHandle1] = profile1;
            if (person2.Profile is { } profile2)
                ancestors[resolvedHandle2] = profile2;
            var ancestorHandles = all.SelectMany(PersonFormatter.CommonAncestorHandles)
                .Distinct()
                .Where(h => h != resolvedHandle1 && h != resolvedHandle2)
                .Take(MaxNamedCommonAncestors);
            var ancestorPeople = await client.GetByHandlesAsync<GrampsPerson>(
                "people", ancestorHandles, p => p.Handle, query: "profile=self");
            foreach (var (handle, person) in ancestorPeople)
            {
                if (person.Profile is { } profile)
                    ancestors[handle] = profile;
            }

            return PersonFormatter.FormatRelationships(
                resolvedHandle1, person1.Profile, resolvedHandle2, person2.Profile, relation, all, ancestors);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static Task<GrampsPerson?> GetPersonProfileAsync(GrampsApiClient client, string handle) =>
        client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{Uri.EscapeDataString(handle)}?profile=self");

    [McpServerTool(Title = "Create Person", ReadOnly = false, Destructive = false)]
    [Description(
        "Create one person with full control: names, gender, links to existing events (with roles), families, " +
        "citations, notes, media and tags, plus attributes, addresses, URLs and associations. " +
        "Returns the new handle, Gramps ID and name, with next steps. Does not check for duplicates: search for the person first, " +
        "and change an existing person with update_person. Events are separate records: create the Birth, Death and other " +
        "events with create_event first and pass them in eventRefs, or attach them later with update_person (linkMode add). To make the person a child of a family, add them to the family's childRefs " +
        "(create_family or update_family). " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreatePerson(
        [Description(FlexibleGrampsName.DescriptionHint)]
        FlexibleGrampsName? primaryName,
        [Description("Gender: Female, Male, or Unknown (default Unknown).")]
        string gender = "Unknown",
        [Description(FlexibleAlternateNameList.DescriptionHint)]
        FlexibleAlternateNameList? alternateNames = null,
        [Description("Existing events this person takes part in, each with a role (Primary by default; Witness, Godparent, …). " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Families where this person is a parent or spouse. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? familyHandles = null,
        [Description("Families where this person is a child. Does not add the person to the family's children: use childRefs on the family for that. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? parentFamilyHandles = null,
        [Description("Media. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citations. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description(FlexibleAddressList.DescriptionHint)]
        FlexibleAddressList? addresses = null,
        [Description(FlexibleUrlList.DescriptionHint)]
        FlexibleUrlList? urls = null,
        [Description(FlexiblePersonRefList.DescriptionHint)]
        FlexiblePersonRefList? personAssociations = null,
        [Description("Mark the person private (default false).")]
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

            await SetVitalEventIndexesAsync(client, request);

            var (handle, grampsId) = await client.PostMutationAsync("/api/people/", request, "Person");
            return ResponseEnvelope.CreateSuccess(
                "Person", handle, grampsId,
                GrampsValueFormatter.FormatName(primary),
                ResponseEnvelope.PersonCreateNextSteps(handle,
                    hasEvents: eventRefArr.Length > 0,
                    hasFamily: request.FamilyList is { Length: > 0 } || request.ParentFamilyList is { Length: > 0 },
                    hasNotes: request.NoteList is { Length: > 0 }));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Person", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing person: names, gender, links to events, families, citations, notes, media and tags, " +
        "attributes, addresses, URLs, associations or the private flag. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing person returns a not-found message. " +
        "Links live on one side: to link an event to this person, update the person (linkMode add), not the event. " +
        ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdatePerson(
        [Description("The person to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New primary name; replaces the whole current one. " + ToolDescriptionFragments.OmitToKeepScalar + " " + FlexibleGrampsName.DescriptionHint)]
        FlexibleGrampsName? primaryName = null,
        [Description("Gender: Female, Male, or Unknown. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? gender = null,
        [Description("Alternate names. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAlternateNameList.DescriptionHint)]
        FlexibleAlternateNameList? alternateNames = null,
        [Description("Events this person takes part in, with roles. In replace mode the list must hold every event the person keeps. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Families where this person is a parent or spouse. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? familyHandles = null,
        [Description("Families where this person is a child. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? parentFamilyHandles = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Addresses. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAddressList.DescriptionHint)]
        FlexibleAddressList? addresses = null,
        [Description("URLs. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleUrlList.DescriptionHint)]
        FlexibleUrlList? urls = null,
        [Description("Associations with other people (godfather, friend, …). " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexiblePersonRefList.DescriptionHint)]
        FlexiblePersonRefList? personAssociations = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace",
        [Description(EventRefOrder.Description)]
        bool sortEvents = false)
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "people");
            var person = await GrampsObjectPatch.LoadAsync(client, $"/api/people/{Uri.EscapeDataString(resolvedHandle)}");
            if (person == null)
                return NotFoundHelper.NotFoundMessage("Person", handle);

            if (primaryName?.Name != null)
                person.Set("primary_name", ConvertNameToRequest(primaryName.Name));
            person.Set("gender", GrampsGenderParser.ParseOptional(gender));
            person.Set("alternate_names", ((GrampsName[]?)alternateNames)?.Select(ConvertNameToRequest).ToArray());
            person.ApplyRefs("event_ref_list", (EventRefRequest[]?)eventRefs, linkMode);
            if (sortEvents)
                await EventRefOrder.SortAsync(client, person.Root);
            person.ApplyHandles("family_list", familyHandles, linkMode);
            person.ApplyHandles("parent_family_list", parentFamilyHandles, linkMode);
            person.ApplyMediaHandles(mediaHandles, linkMode);
            person.Set("address_list", (GrampsAddress[]?)addresses);
            person.ReplaceAttributes(attributes);
            person.ApplyHandles("citation_list", citationHandles, linkMode);
            person.ApplyHandles("note_list", noteHandles, linkMode);
            person.ApplyHandles("tag_list", tagHandles, linkMode);
            person.Set("urls", (GrampsUrl[]?)urls);
            person.ApplyRefs("person_ref_list", (GrampsPersonRef[]?)personAssociations, linkMode);
            person.Set("private", isPrivate);

            await person.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Person", person.Handle, person.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// Gramps Web sets birth_ref_index and death_ref_index when it saves a person with PUT but not with POST, so a
    /// new person would show no birth or death until its next update. Like Gramps <c>set_birth_death_index</c>, takes
    /// the first Birth and the first Death the person has the Primary role in; -1 when there is none.
    /// </summary>
    private static async Task SetVitalEventIndexesAsync(GrampsApiClient client, CreatePersonRequest request)
    {
        var refs = request.EventRefList ?? [];
        var primary = refs.Where(r => PersonFormatter.IsPrimaryRole(r.Role)).Select(r => r.Ref).ToArray();
        if (primary.Length == 0)
            return;

        var events = await client.GetByHandlesAsync<GrampsEvent>("events", primary, e => e.Handle);
        for (var i = 0; i < refs.Length; i++)
        {
            if (!PersonFormatter.IsPrimaryRole(refs[i].Role)
                || refs[i].Ref?.Trim() is not { } eventHandle
                || !events.TryGetValue(eventHandle, out var evt))
                continue;
            if (evt.Type == "Birth" && request.BirthRefIndex < 0)
                request.BirthRefIndex = i;
            else if (evt.Type == "Death" && request.DeathRefIndex < 0)
                request.DeathRefIndex = i;
        }
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

}
