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

    [McpServerTool(Title = "Create Family", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a family: a couple (father and mother, each optional) with their relationship type, children and family events " +
        "such as Marriage. Returns the new handle, Gramps ID and a label, with next steps. People and events must already exist " +
        "(create_person, create_event). Does not check for duplicates: look at the parents' families first, " +
        "and change an existing family with update_family. Children are added here through childRefs, " +
        "not through the child's parentFamilyHandles. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateFamily(
        [Description("The father, optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("The mother, optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type: Married (default), Unmarried, Civil Union or Unknown. " + ToolDescriptionFragments.KnownType)]
        string? relationshipType = "Married",
        [Description("Children, each with its relation to the father and mother (Birth by default; Adopted, Stepchild, …). " + FlexibleChildRefList.DescriptionHint)]
        FlexibleChildRefList? childRefs = null,
        [Description("Existing family events such as Marriage or Divorce. " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
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
        [Description("Mark the family private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (relationshipType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(relationshipType, "family_relation_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var childRefArr = (GrampsChildRef[]?)childRefs;
            var eventRefArr = (EventRefRequest[]?)eventRefs ?? [];
            var resolvedFatherHandle = fatherHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(fatherHandle, client, "people");
            var resolvedMotherHandle = motherHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(motherHandle, client, "people");

            var request = new CreateFamilyRequest
            {
                FatherHandle = resolvedFatherHandle,
                MotherHandle = resolvedMotherHandle,
                ChildRefList = childRefArr is { Length: > 0 } ? childRefArr : null,
                EventRefList = eventRefArr.Length > 0 ? eventRefArr : null,
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate,
                Relationship = relationshipType
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/families/", request, "Family");
            var relLabel = string.IsNullOrWhiteSpace(relationshipType)
                ? "Unknown"
                : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, relationshipType);
            return ResponseEnvelope.CreateSuccess(
                "Family", handle, grampsId,
                relLabel, ResponseEnvelope.FamilyCreateNextSteps(handle,
                    hasChildren: childRefArr is { Length: > 0 }, hasEvents: eventRefArr.Length > 0));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Family", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing family: father, mother, relationship type, children, family events, citations, notes, media, tags, " +
        "attributes or the private flag. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing family returns a not-found message. " +
        "To add one child, pass it in childRefs with linkMode add. " + ToolDescriptionFragments.InputGuide)]
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
        [Description("Family events (Marriage, Divorce, …). " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
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
            if (relationshipType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(relationshipType, "family_relation_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

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
            if (sortEvents)
                await EventRefOrder.SortAsync(client, family.Root);
            family.ApplyMediaHandles(mediaHandles, linkMode);
            family.ReplaceAttributes(attributes);
            family.ApplyHandles("citation_list", citationHandles, linkMode);
            family.ApplyHandles("note_list", noteHandles, linkMode);
            family.ApplyHandles("tag_list", tagHandles, linkMode);
            family.Set("private", isPrivate);

            await family.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Family", family.Handle, family.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
