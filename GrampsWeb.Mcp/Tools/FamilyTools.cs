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
        "Get family unit data by handle. Returns father_handle, mother_handle, " +
        "child handles with relationship types (frel/mrel), relationship type, " +
        "and handles of linked events, notes and tags. " +
        "Use get_family_extended for resolved member names. " +
        "Use list_objects('families') or search() to find family handles.")]
    public static async Task<string> GetFamily(
        [Description("Family handle — use list_objects('families') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var family = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{handle}");
            return family == null
                ? $"Family not found: {handle}"
                : await FamilyFormatter.FormatFamilyFullAsync(family, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get complete family data with all member names and events resolved in a single request. " +
        "Returns resolved father name, mother name, children names, event dates and place names. " +
        "After the bulk response, missing first-level unwraps are filled: citations (source etc. via per-citation extend=all), " +
        "events (place via per-event extend=place), and media objects (GET /api/media/… when extended.media is absent). " +
        "Use for comprehensive family overview. Slower than get_family.")]
    public static async Task<string> GetFamilyExtended(
        [Description("Family handle")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var family = await client.GetOrNullIfNotFoundAsync<GrampsFamilyExtended>($"/api/families/{handle}?extend=all");
            if (family == null)
                return $"Family not found: {handle}";
            await ExtendedEntityEnrichment.EnrichFamilyExtendedAsync(family, client);
            return await FamilyFormatter.FormatFamilyExtended(family, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get chronological timeline of events for a family. " +
        "events: filter by category — vital, family, religious, vocational, academic, travel, legal, residence, other, custom. " +
        "dates: date range; zero-padding in month/day is normalized for the API (1999/1/1 not 1999/01/01). " +
        "By default, events with sortval 0 are still included (discard_empty=false), matching person timeline behavior. " +
        "Rows include [event: handle] when the API provides handles, for get_event follow-up.")]
    public static async Task<string> GetFamilyTimeline(
        [Description("Family handle")]
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
        "Create family unit. Call get_types() for valid relationship_type values. " +
        "childRefTypes: birth relationship of each child (Birth, Adopted, Stepchild...). " +
        "childHandles and childRefTypes must be same length. " +
        "Returns family handle.")]
    public static async Task<string> CreateFamily(
        [Description("Father person handle (optional)")]
        string? fatherHandle = null,
        [Description("Mother person handle (optional)")]
        string? motherHandle = null,
        [Description("Relationship type: Married, Unmarried, Civil Union, Unknown (default: Married)")]
        string? relationshipType = "Married",
        [Description("Child person handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? childHandles = null,
        [Description("Array of child relationship types (Birth, Adopted, Stepchild, etc). Must match childHandles length")]
        string[]? childRefTypes = null,
        [Description("Note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            // Build child_ref_list
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

            var request = new CreateFamilyRequest
            {
                FatherHandle = fatherHandle,
                MotherHandle = motherHandle,
                ChildRefList = childRefList.Count > 0 ? childRefList.ToArray() : null,
                NoteList = noteHandles,
                TagList = tagHandles,
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
        "Update existing family. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects.")]
    public static async Task<string> UpdateFamily(
        [Description("Family handle")]
        string handle,
        [Description("Update father handle")]
        string? fatherHandle = null,
        [Description("Update mother handle")]
        string? motherHandle = null,
        [Description("Update relationship type")]
        string? relationshipType = null,
        [Description("Replace child references. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? childHandles = null,
        [Description("Child relationship types to match childHandles")]
        string[]? childRefTypes = null,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
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
                EventRefList = GrampsRequestMapping.ToEventRefRequests(family.EventRefList),
                MediaList = family.MediaList,
                AttributeList = GrampsRequestMapping.ToAttributeRequests(family.AttributeList),
                CitationList = family.CitationList,
                NoteList = (string[]?)noteHandles ?? family.NoteList,
                TagList = (string[]?)tagHandles ?? family.TagList,
                Private = family.Private,
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
        "Delete a family unit. Will warn if family members still reference it. " +
        "Does NOT automatically remove family from member Person objects.")]
    public static async Task<string> DeleteFamily(
        [Description("Family handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
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
