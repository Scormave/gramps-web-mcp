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
/// Covers get_family, get_family_extended, and get_family_timeline.
/// </summary>
[McpServerToolType]
public static class FamilyTools
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tools
    // ─────────────────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Read-only: one family by handle (parents, children, relationship type, events). " +
        "With extended=true, resolves member names, event dates/places, citations, media. " +
        "Default extended=false returns handles only (faster).")]
    public static async Task<string> GetFamily(
        [Description("Family handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Resolve linked names/events/places inline. Default: false.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (extended)
            {
                var family = await client.GetOrNullIfNotFoundAsync<GrampsFamilyExtended>($"/api/families/{handle}?extend=all");
                if (family == null)
                    return NotFoundHelper.NotFoundMessage("Family", handle);
                await ExtendedEntityEnrichment.EnrichFamilyExtendedAsync(family, client);
                return await FamilyFormatter.FormatFamilyExtended(family, client);
            }
            else
            {
                var family = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{handle}");
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

    [McpServerTool]
    [Description(
        "Read-only: chronological events for one family. " +
        "events filters by category (vital, family, religious, vocational, academic, travel, legal, residence, other, custom). " +
        "dates uses YYYY/M/D ranges; leading zeros stripped for API. Undated events included by default (same behavior as person timeline). " +
        "Rows may include event handles for get_event.")]
    public static async Task<string> GetFamilyTimeline(
        [Description("Family handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Event categories: vital, family, religious, vocational, academic, travel, legal, residence, other, custom")]
        string[]? events = null,
        [Description("Date range filter as 'YYYY/MM/DD-YYYY/MM/DD'")]
        string? dates = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var qs = PersonTools.BuildTimelineQueryString(events, null, null, dates);
            var timeline = await client.GetOrNullIfNotFoundAsync<GrampsTimelineEntry[]>(
                $"/api/families/{handle}/timeline{qs}");
            if (timeline == null)
                return NotFoundHelper.NotFoundMessage("Family", handle);
            if (timeline.Length == 0)
                return $"No timeline events found for family {handle}";
            return TimelineFormatter.FormatTimelineChronological(timeline);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a family unit (write). Returns family handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide + " " +
        "Provide childRefs and eventRefs to include handle + relationship metadata in one field.")]
    public static async Task<string> CreateFamily(
        [Description("Father person handle. Optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("Mother person handle. Optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type: Married, Unmarried, Civil Union, Unknown (default: Married)")]
        string? relationshipType = "Married",
        [Description("Child links for this family. " + FlexibleChildRefList.DescriptionHint)]
        FlexibleChildRefList? childRefs = null,
        [Description("Events to link to this family. " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
        [Description("Media handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark record private (default: false)")]
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

            var request = new CreateFamilyRequest
            {
                FatherHandle = fatherHandle,
                MotherHandle = motherHandle,
                ChildRefList = childRefArr is { Length: > 0 } ? childRefArr : null,
                EventRefList = eventRefArr.Length > 0 ? eventRefArr : null,
                MediaList = mediaHandles,
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
                relLabel, ResponseEnvelope.FamilyCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update an existing family (write). Only pass fields you want to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        "Replacing childRefs or eventRefs replaces the full corresponding list. " +
        ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdateFamily(
        [Description("Family handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Father person handle. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("Mother person handle. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type string. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.CallGetTypes)]
        string? relationshipType = null,
        [Description("Replace all children. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleChildRefList.DescriptionHint)]
        FlexibleChildRefList? childRefs = null,
        [Description("Replace family–event links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleEventRefList.DescriptionHint)]
        FlexibleEventRefList? eventRefs = null,
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
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (relationshipType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(relationshipType, "family_relation_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var family = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{handle}");
            if (family == null)
                return NotFoundHelper.NotFoundMessage("Family", handle);

            var updateRequest = new CreateFamilyRequest
            {
                Class = "Family",
                Handle = family.Handle,
                GrampsId = family.GrampsId,
                Change = family.Change,
                FatherHandle = fatherHandle ?? family.FatherHandle,
                MotherHandle = motherHandle ?? family.MotherHandle,
                ChildRefList = childRefs != null ? (GrampsChildRef[]?)childRefs : family.ChildRefList,
                EventRefList = eventRefs != null
                    ? (EventRefRequest[]?)eventRefs
                    : GrampsRequestMapping.ToEventRefRequests(family.EventRefList),
                MediaList = (string[]?)mediaHandles ?? family.MediaList,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(family.AttributeList),
                CitationList = (string[]?)citationHandles ?? family.CitationList,
                NoteList = (string[]?)noteHandles ?? family.NoteList,
                TagList = (string[]?)tagHandles ?? family.TagList,
                Private = isPrivate ?? family.Private,
                Relationship = relationshipType ?? family.Relationship
            };

            await client.PutMutationAsync($"/api/families/{handle}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Family", family.Handle, family.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a family (destructive). Blocked when backlinks exist unless force=true. " +
        "Does not remove this family from person records automatically; fix person links separately if needed.")]
    public static async Task<string> DeleteFamily(
        [Description("Family handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite remaining references (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Family", "families", handle, force);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
