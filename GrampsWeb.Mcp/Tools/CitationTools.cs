using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools.Parsing;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Citation objects—the links between sources and genealogical objects.
/// </summary>
[McpServerToolType]
public static class CitationTools
{
    [McpServerTool]
    [Description(
        "Read-only: one citation (source title/handle, page, confidence, access date). " +
        "Citations connect sources to facts on people, events, places, etc.")]
    public static async Task<string> GetCitation(
        [Description("Citation handle. " + ToolDescriptionFragments.HandleDiscovery + " For one source's citations use list_objects('citations', sourceHandle: ...).")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var citation = await client.GetOrNullIfNotFoundAsync<GrampsCitation>($"/api/citations/{handle}");
            return citation == null
                ? NotFoundHelper.NotFoundMessage("Citation", handle)
                : await CitationFormatter.FormatCitationFull(citation, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a citation (write). Returns handle and Gramps ID. " +
        "sourceHandle must be an existing source (create_source first). " +
        "Attach to people/events/places via their citationHandles on create/update. " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> CreateCitation(
        [Description("Source handle (required). " + ToolDescriptionFragments.HandleDiscovery)]
        string sourceHandle,
        [Description("Page reference within source (optional)")]
        string? page = null,
        [Description("Confidence: Very Low, Low, Normal, High, or Very High (default: Normal)")]
        string confidence = "Normal",
        [Description("Access or reference date text. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Citation text / transcript (optional)")]
        string? text = null,
        [Description("Media handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceHandle))
                throw McpToolErrors.ValidationError("Error: sourceHandle is required");

            var confidenceLevel = Math.Clamp(CitationConfidenceParser.ParseRequired(confidence), 0, 4);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso);

            var request = new CreateCitationRequest
            {
                Source = sourceHandle,
                Page = page,
                Confidence = confidenceLevel,
                Date = dateRequest,
                Text = text,
                MediaList = mediaHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsCitation>("/api/citations/", request, "Citation");
            return ResponseEnvelope.CreateSuccess("Citation", response.Handle, response.GrampsId,
                response.Page, ResponseEnvelope.CitationCreateNextSteps(response.Handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update a citation (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdateCitation(
        [Description("Citation handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Source handle. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? sourceHandle = null,
        [Description("Page within source. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? page = null,
        [Description("Confidence: Very Low, Low, Normal, High, Very High. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? confidence = null,
        [Description("Date text. Omit to keep. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Transcript or citation text. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? text = null,
        [Description("Replace media. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var citation = await client.GetOrNullIfNotFoundAsync<GrampsCitation>($"/api/citations/{handle}");
            if (citation == null)
                return NotFoundHelper.NotFoundMessage("Citation", handle);

            var finalConfidence = Math.Clamp(
                CitationConfidenceParser.ParseOptional(confidence) ?? citation.Confidence,
                0,
                4);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso)
                : GrampsRequestMapping.ToDateRequestOrNull(citation.Date);

            var updateRequest = new CreateCitationRequest
            {
                Class = "Citation",
                Handle = citation.Handle,
                GrampsId = citation.GrampsId,
                Change = citation.Change,
                Source = sourceHandle ?? citation.Source,
                Page = page ?? citation.Page,
                Confidence = finalConfidence,
                Date = dateRequest,
                Text = text ?? citation.Text,
                MediaList = (string[]?)mediaHandles ?? citation.MediaList,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(citation.AttributeList),
                NoteList = (string[]?)noteHandles ?? citation.NoteList,
                TagList = (string[]?)tagHandles ?? citation.TagList,
                Private = isPrivate ?? citation.Private
            };

            var response = await client.PutMutationAsync<GrampsCitation>($"/api/citations/{handle}", updateRequest, "Citation");
            return ResponseEnvelope.UpdateSuccess("Citation", response.Handle, response.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a citation (destructive). Blocked when still linked from other objects unless force=true.")]
    public static async Task<string> DeleteCitation(
        [Description("Citation handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite backlinks (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/citations/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return NotFoundHelper.NotFoundMessage("Citation", handle);
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
                return $"⚠️ Cannot delete citation [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_citation(handle, force=true).";
            }

            await client.DeleteAsync($"/api/citations/{handle}");
            return ResponseEnvelope.DeleteSuccess("Citation", handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
