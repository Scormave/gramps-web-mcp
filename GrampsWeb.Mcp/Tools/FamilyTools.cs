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
                    return $"Family not found: {handle}";
                await ExtendedEntityEnrichment.EnrichFamilyExtendedAsync(family, client);
                return await FamilyFormatter.FormatFamilyExtended(family, client);
            }
            else
            {
                var family = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{handle}");
                return family == null
                    ? $"Family not found: {handle}"
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
                return $"Family not found: {handle}";
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
        "childHandles and childRefTypes must have the same length (child relationship: Birth, Adopted, Stepchild, …). " +
        "eventRefHandles and eventRefRoles must have the same length (same pattern as create_person).")]
    public static async Task<string> CreateFamily(
        [Description("Father person handle. Optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? fatherHandle = null,
        [Description("Mother person handle. Optional. " + ToolDescriptionFragments.HandleDiscovery)]
        string? motherHandle = null,
        [Description("Relationship type: Married, Unmarried, Civil Union, Unknown (default: Married)")]
        string? relationshipType = "Married",
        [Description("Child person handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? childHandles = null,
        [Description("One relationship type per childHandles entry (same length): Birth, Adopted, Stepchild, …")]
        string[]? childRefTypes = null,
        [Description("Events to link to this family. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? eventRefHandles = null,
        [Description("Parallel to eventRefHandles (same length). Shorter arrays are padded with Primary.")]
        string[]? eventRefRoles = null,
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

            var childRefList = new List<GrampsChildRef>();
            var childHandleArray = (string[]?)childHandles;
            if (childHandleArray?.Length > 0)
            {
                for (int i = 0; i < childHandleArray.Length; i++)
                {
                    var refType = childRefTypes?.Length > i ? childRefTypes[i] : "Birth";
                    childRefList.Add(new GrampsChildRef
                    {
                        Ref = childHandleArray[i],
                        FatherRelType = refType,
                        MotherRelType = refType
                    });
                }
            }

            var eventRefArr = GrampsRequestMapping.BuildEventRefList((string[]?)eventRefHandles, eventRefRoles);

            var request = new CreateFamilyRequest
            {
                FatherHandle = fatherHandle,
                MotherHandle = motherHandle,
                ChildRefList = childRefList.Count > 0 ? childRefList.ToArray() : null,
                EventRefList = eventRefArr.Length > 0 ? eventRefArr : null,
                MediaList = mediaHandles,
                CitationList = citationHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate,
                Relationship = relationshipType
            };

            var response = await client.PostMutationAsync<GrampsFamily>("/api/families/", request, "Family");
            var relLabel = string.IsNullOrWhiteSpace(response.Relationship)
                ? "Unknown"
                : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, response.Relationship);
            return $"Family created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Relationship: {relLabel}\n" +
                   $"Father: {response.FatherHandle ?? "—"}\n" +
                   $"Mother: {response.MotherHandle ?? "—"}\n" +
                   $"Children: {response.ChildRefList?.Length ?? 0}";
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
        "Replacing childHandles replaces the full child list; childRefTypes must match length. " +
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
        [Description("Replace all children. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? childHandles = null,
        [Description("One type per childHandles entry (same length). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string[]? childRefTypes = null,
        [Description("Replace family–event links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? eventRefHandles = null,
        [Description("One role per eventRefHandles entry (same length). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string[]? eventRefRoles = null,
        [Description("Replace media links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace citation links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace note links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tag links. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        GrampsAttribute[]? attributes = null,
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
                return $"Family not found: {handle}";

            var childRefList = new List<GrampsChildRef>();
            var childHandleArrayUpdate = (string[]?)childHandles;
            if (childHandleArrayUpdate != null && childHandleArrayUpdate.Length > 0)
            {
                for (int i = 0; i < childHandleArrayUpdate.Length; i++)
                {
                    var refType = childRefTypes?.Length > i ? childRefTypes[i] : "Birth";
                    childRefList.Add(new GrampsChildRef
                    {
                        Ref = childHandleArrayUpdate[i],
                        FatherRelType = refType,
                        MotherRelType = refType
                    });
                }
            }

            var updateRequest = new CreateFamilyRequest
            {
                Class = "Family",
                Handle = family.Handle,
                GrampsId = family.GrampsId,
                Change = family.Change,
                FatherHandle = fatherHandle ?? family.FatherHandle,
                MotherHandle = motherHandle ?? family.MotherHandle,
                ChildRefList = childHandles != null ? childRefList.ToArray() : family.ChildRefList,
                EventRefList = eventRefHandles != null
                    ? GrampsRequestMapping.BuildEventRefList((string[]?)eventRefHandles, eventRefRoles)
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

            var response = await client.PutMutationAsync<GrampsFamily>($"/api/families/{handle}", updateRequest, "Family");
            var relLabel = string.IsNullOrWhiteSpace(response.Relationship)
                ? "Unknown"
                : await GrampsDefaultTypeLabels.FormatFamilyRelationTypeAsync(client, response.Relationship);
            return $"Family updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Relationship: {relLabel}";
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
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/families/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Family not found: {handle}";
            var response = payload.Value;

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
                        }
                    }
                }
            }

            if (hasBacklinks && !force)
            {
                return $"⚠️ Cannot delete family [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_family(handle, force=true).\n" +
                       $"Warning: family will NOT be removed from Person.family_list.";
            }

            await client.DeleteAsync($"/api/families/{handle}");
            return $"Family deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
