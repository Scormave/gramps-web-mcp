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
        "Read-only: list a person's ancestors or descendants up to N generations, with names and vital dates/places. " +
        "Set direction to ancestors or descendants. Each row includes a generation and optional kinship labels. " +
        "Ancestors follow parent-family links; descendants follow children on families where the person is a parent.")]
    public static async Task<string> GetPersonTree(
        [Description("Root person handle or Gramps ID. " + ToolDescriptionFragments.HandleDiscovery)]
        string person,
        [Description("Tree direction: ancestors | descendants.")]
        string direction,
        [Description("Number of generations to include (default: 3, max: 10)")]
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
        "Read-only: how two people are related, read as 'person 2 is the X of person 1' " +
        "(e.g. 'third cousin twice removed', 'husband'), with generations to the common ancestor " +
        "and every relationship found with its common ancestors by name; or a clear message if unrelated. " +
        "Searches blood relatives up to 15 generations, plus spouses.")]
    public static async Task<string> GetRelations(
        [Description("First person handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle1,
        [Description("Second person handle. " + ToolDescriptionFragments.HandleDiscovery)]
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
        "Update an existing person (write). Only include arguments you want to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        "With linkMode=replace, eventRefs replaces the full event list. " +
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
        [Description("Linked all person–event links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Linked families where this person is parent/spouse. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? familyHandles = null,
        [Description("Linked parent (child-of) families. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? parentFamilyHandles = null,
        [Description("Linked media links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Linked citation links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Linked note links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Linked tag links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Replace addresses. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAddressList.DescriptionHint)]
        FlexibleAddressList? addresses = null,
        [Description("Replace URLs. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleUrlList.DescriptionHint)]
        FlexibleUrlList? urls = null,
        [Description("Linked person associations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexiblePersonRefList.DescriptionHint)]
        FlexiblePersonRefList? personAssociations = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            using var updateLease = await client.BeginUpdateAsync();
            // Get current person first
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "people");
            var person = await client.GetOrNullIfNotFoundAsync<GrampsPerson>(
                $"/api/people/{Uri.EscapeDataString(resolvedHandle)}");
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
                EventRefList = LinkUpdates.Apply(GrampsRequestMapping.ToEventRefRequests(person.EventRefList),
                    (EventRefRequest[]?)eventRefs, linkMode, x => x.Ref),
                FamilyList = LinkUpdates.Apply(person.FamilyList, (string[]?)familyHandles, linkMode, x => x),
                ParentFamilyList = LinkUpdates.Apply(GrampsRequestMapping.ToParentFamilyHandles(person.ParentFamilyList), (string[]?)parentFamilyHandles, linkMode, x => x),
                MediaList = LinkUpdates.Apply(GrampsRequestMapping.ToMediaRefRequests(person.MediaList),
                    mediaHandles is null ? null : (GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, person.MediaList) ?? []), linkMode, x => x.Ref),
                AddressList = addresses is null ? person.AddressList : (GrampsAddress[]?)addresses,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(person.AttributeList),
                CitationList = LinkUpdates.Apply(person.CitationList, (string[]?)citationHandles, linkMode, x => x),
                NoteList = LinkUpdates.Apply(person.NoteList, (string[]?)noteHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(person.TagList, (string[]?)tagHandles, linkMode, x => x),
                UrlList = urls is null ? person.UrlList : (GrampsUrl[]?)urls,
                PersonRefList = LinkUpdates.Apply(person.PersonRefList, (GrampsPersonRef[]?)personAssociations, linkMode, x => x.Ref),
                Private = isPrivate ?? person.Private
            };

            await client.PutMutationAsync($"/api/people/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Person", person.Handle, person.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
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
