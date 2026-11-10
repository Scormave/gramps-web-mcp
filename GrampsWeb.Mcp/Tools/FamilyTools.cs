using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Family objects from the Gramps Web API.
/// Covers family mutations.
/// </summary>
[McpServerToolType]
public static class FamilyTools
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tools
    // ─────────────────────────────────────────────────────────────────────────

    [Description(
        "Read-only: one family by handle (parents, children, relationship type, events). " +
        "With extended=true, resolves member names, event dates/places, citations, media. " +
        "Default extended=false names parents and children with life dates and lists event types and dates in one request.")]
    internal static async Task<string> ReadFamilyAsync(
        [Description("Family handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Resolve linked names/events/places inline. Default: false.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "families");
            if (extended)
            {
                var family = await client.GetOrNullIfNotFoundAsync<GrampsFamilyExtended>(
                    $"/api/families/{Uri.EscapeDataString(resolvedHandle)}?extend=all");
                if (family == null)
                    return NotFoundHelper.NotFoundMessage("Family", handle);
                await ExtendedEntityEnrichment.EnrichFamilyExtendedAsync(family, client);
                return await FamilyFormatter.FormatFamilyExtended(family, client);
            }
            else
            {
                // The profile names members and events in the same response.
                var family = await client.GetOrNullIfNotFoundAsync<GrampsFamily>(
                    $"/api/families/{Uri.EscapeDataString(resolvedHandle)}?profile=self,events");
                return family == null
                    ? NotFoundHelper.NotFoundMessage("Family", handle)
                    : await FamilyFormatter.FormatFamilyFullAsync(family, client);
            }
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Family", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Create a family: a couple (father and mother, each optional) with their relationship type, children and family events " +
        "such as Marriage. People and events must already exist (create_person, create_event). Gramps Web links the father, " +
        "mother and children back to the family in the same save, and if one of them does not exist it refuses the family and " +
        "nothing is saved. relationshipType stays Unknown unless a source records the marriage: then pass Married and link the " +
        "Marriage event in eventRefs, where a family event takes the Family role, which is what makes Gramps and Gramps Web " +
        "show the family among the event's participants. Each child in childRefs carries its own relation to the father and " +
        "to the mother, Birth unless the source says Adopted, Stepchild or another; children keep the order given, so list " +
        "them by birth. Citations, notes, media, tags and attributes passed here belong to the family itself, not to its " +
        "members or events. Returns the new handle, Gramps ID and stored relationship type; when children or events were " +
        "left out, it also lists the update_family calls that add them with linkMode add. " +
        "Does not check for duplicates: look at the parents' families first, " +
        "and change an existing family with update_family. Children are added here through childRefs, " +
        "not through the child's parentFamilyHandles. " + ToolDescriptionFragments.BatchCreate + " " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateFamily(
        [Description("The father, optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("The mother, optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type: Married, Unmarried, Civil Union or Unknown (default, as in Gramps). Pass Married when " +
                     "a source records the marriage. " + ToolDescriptionFragments.KnownType)]
        string? relationshipType = "Unknown",
        [Description("Children, each with its relation to the father and mother (Birth by default; Adopted, Stepchild, …). " + FlexibleChildRefList.DescriptionHint)]
        FlexibleChildRefList? childRefs = null,
        [Description("Existing family events such as Marriage or Divorce. " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Media. " + FlexibleMediaLinkList.DescriptionHint)]
        FlexibleMediaLinkList? mediaHandles = null,
        [Description("Citations. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark the family private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var request = await BuildFamilyRequestAsync(fatherHandle, motherHandle, relationshipType, childRefs, eventRefs,
                mediaHandles, citationHandles, noteHandles, tagHandles, attributes, isPrivate, client);
            var (handle, grampsId) = await client.PostMutationAsync("/api/families/", request, "Family");
            var relLabel = await FormatRelationshipAsync(request.Relationship, client);
            return ResponseEnvelope.CreateSuccess(
                "Family", handle, grampsId,
                relLabel, ResponseEnvelope.FamilyCreateNextSteps(handle,
                    hasChildren: request.ChildRefList is { Length: > 0 }, hasEvents: request.EventRefList is { Length: > 0 }));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>The checked request body of create_family; create_records sends it too.</summary>
    internal static async Task<CreateFamilyRequest> BuildFamilyRequestAsync(
        string? fatherHandle, string? motherHandle, string? relationshipType,
        FlexibleChildRefList? childRefs, FlexibleEventRefList? eventRefs, FlexibleMediaLinkList? mediaHandles,
        FlexibleHandleList? citationHandles, FlexibleHandleList? noteHandles, FlexibleHandleList? tagHandles,
        FlexibleAttributeList? attributes, bool isPrivate, GrampsApiClient client)
    {
        if (relationshipType != null)
            relationshipType = await KnownTypes.ResolveAsync(relationshipType, "family_relation_types", client);

        var childRefArr = (GrampsChildRef[]?)childRefs;
        await KnownTypes.CheckChildRefsAsync(childRefArr, client);
        var eventRefArr = (EventRefRequest[]?)eventRefs ?? [];
        await EventRoles.CheckAsync(eventRefArr, client, defaultRole: EventRoles.Family);
        // A new family has no stored roles to keep, so a link object without a role takes Family too.
        foreach (var eventRef in eventRefArr.Where(r => string.IsNullOrWhiteSpace(r.Role)))
            eventRef.Role = EventRoles.Family;
        await KnownTypes.CheckAttributesAsync(attributes, client);
        await KnownTypes.CheckMediaLinksAsync(mediaHandles, client);
        var resolvedFatherHandle = fatherHandle is null
            ? null
            : await HandleResolver.ResolveToHandleAsync(fatherHandle, client, "people");
        var resolvedMotherHandle = motherHandle is null
            ? null
            : await HandleResolver.ResolveToHandleAsync(motherHandle, client, "people");

        return new CreateFamilyRequest
        {
            FatherHandle = resolvedFatherHandle,
            MotherHandle = resolvedMotherHandle,
            ChildRefList = childRefArr is { Length: > 0 } ? childRefArr : null,
            EventRefList = eventRefArr.Length > 0 ? eventRefArr : null,
            MediaList = GrampsRequestMapping.ToMediaRefRequests(mediaHandles),
            CitationList = citationHandles,
            NoteList = noteHandles,
            TagList = tagHandles,
            AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
            Private = isPrivate,
            Relationship = relationshipType
        };
    }

    /// <summary>The label of a family's relationship type: Unknown when none is set.</summary>
    internal static async Task<string> FormatRelationshipAsync(string? relationshipType, GrampsApiClient client) =>
        string.IsNullOrWhiteSpace(relationshipType)
            ? "Unknown"
            : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, relationshipType);

    [McpServerTool(Title = "Update Family", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing family's father, mother, relationship type, children, family events, attributes or links to " +
        "citations, notes, media and tags. For a new family use create_family; to remove one, delete_object. " +
        "To add one child, pass it in childRefs with linkMode add; in replace mode childRefs must hold every child the " +
        "family keeps. attributes replace the whole list whatever linkMode says, and sort reorders children, events, " +
        "citations or media by date without changing which are linked. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Gramps Web keeps the people's side in step in the same save: a new father, mother or child gets the family on " +
        "their record, and one replaced or removed loses it; a father, mother or new child that does not exist makes it " +
        "refuse the change, and nothing is saved. Returns a short header with the type, action updated, handle and " +
        "Gramps ID, not the changed family: read it back with get_object. A missing family returns a not-found message. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateFamily(
        [Description("The family to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New father. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("New mother. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type (Married, Unmarried, Civil Union, Unknown). " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? relationshipType = null,
        [Description("Children with their relations to the parents. In replace mode the list must hold every child the family keeps. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleChildRefList.DescriptionHint)]
        FlexibleChildRefList? childRefs = null,
        [Description("Family events (Marriage, Divorce, …). " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleEventRefList.DescriptionHint + " " + FlexibleEventRefList.UpdateHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleMediaLinkList.DescriptionHint + " " + FlexibleMediaLinkList.UpdateHint)]
        FlexibleMediaLinkList? mediaHandles = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace",
        [Description(LinkOrder.FamilyDescription)]
        FlexibleSortList? sort = null)
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            var sortedLists = LinkOrder.Parse(sort, LinkOrder.FamilyLists);
            await KnownTypes.CheckChildRefsAsync(childRefs, client, linkMode);
            await EventRoles.CheckAsync(eventRefs, client, linkMode, EventRoles.Family);
            await KnownTypes.CheckAttributesAsync(attributes, client);
            await KnownTypes.CheckMediaLinksAsync(mediaHandles, client, linkMode);
            using var updateLease = await client.BeginUpdateAsync();
            if (relationshipType != null)
                relationshipType = await KnownTypes.ResolveAsync(relationshipType, "family_relation_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "families");
            var resolvedFatherHandle = fatherHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(fatherHandle, client, "people");
            var resolvedMotherHandle = motherHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(motherHandle, client, "people");
            var family = await GrampsObjectPatch.LoadAsync(client, $"/api/families/{Uri.EscapeDataString(resolvedHandle)}");
            if (family == null)
                return NotFoundHelper.NotFoundMessage("Family", handle);

            family.Set("father_handle", resolvedFatherHandle);
            family.Set("mother_handle", resolvedMotherHandle);
            family.Set("type", relationshipType);
            family.ApplyRefs("child_ref_list", (GrampsChildRef[]?)childRefs, linkMode);
            family.ApplyRefs("event_ref_list", (EventRefRequest[]?)eventRefs, linkMode);
            if (eventRefs is not null)
                EventRoles.SetMissingFamilyRoles(family.Root);
            family.ApplyMediaLinks(mediaHandles, linkMode);
            family.ReplaceAttributes(attributes);
            family.ApplyHandles("citation_list", citationHandles, linkMode);
            family.ApplyHandles("note_list", noteHandles, linkMode);
            family.ApplyHandles("tag_list", tagHandles, linkMode);
            family.Set("private", isPrivate);
            await LinkOrder.SortAsync(client, family.Root, sortedLists);

            await family.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Family", family.Handle, family.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
