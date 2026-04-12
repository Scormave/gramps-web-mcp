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
        "Get citation data by handle. Returns the source title and handle, page reference within that source, " +
        "confidence level (Very Low/Low/Normal/High/Very High), and access date. " +
        "Citations link sources to genealogical facts.")]
    public static async Task<string> GetCitation(
        [Description("Citation handle — use list_objects('citations', sourceHandle: ...) or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var citation = await client.GetOrNullIfNotFoundAsync<GrampsCitation>($"/api/citations/{handle}");
            return citation == null
                ? $"Citation not found: {handle}"
                : await CitationFormatter.FormatCitationFull(citation, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a citation linking a source to evidence. " +
        "sourceHandle: must be an existing source (use create_source first). " +
        "confidence: Very Low, Low, Normal, High, or Very High (default: Normal). " +
        "Returns citation handle — link to person/event via update. " +
        "Access date strings: get_date_input_guide().")]
    public static async Task<string> CreateCitation(
        [Description("Source handle — must exist (create via create_source first)")]
        string sourceHandle,
        [Description("Page reference within source (optional)")]
        string? page = null,
        [Description("Confidence: Very Low, Low, Normal, High, or Very High (default: Normal)")]
        string confidence = "Normal",
        [Description("Access date as text (optional). Formats: get_date_input_guide().")]
        string? date = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Citation text / transcript (optional)")]
        string? text = null,
        [Description("Media handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Custom attributes (type + value)")]
        GrampsAttribute[]? attributes = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceHandle))
                throw McpToolErrors.ValidationError("Error: sourceHandle is required");

            var confidenceLevel = Math.Clamp(CitationConfidenceParser.ParseRequired(confidence), 0, 4);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder);

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
                AttributeList = GrampsRequestMapping.ToAttributeRequests(attributes),
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsCitation>("/api/citations/", request, "Citation");
            var confidenceLabel = CitationFormatter.ConfidenceLabels[Math.Clamp(response.Confidence, 0, 4)];
            return $"Citation created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Source: {response.Source}\n" +
                   $"Confidence: {confidenceLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing citation. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects. " +
        "Date strings: get_date_input_guide().")]
    public static async Task<string> UpdateCitation(
        [Description("Citation handle")]
        string handle,
        [Description("Update source handle")]
        string? sourceHandle = null,
        [Description("Update page reference")]
        string? page = null,
        [Description("Update confidence: Very Low, Low, Normal, High, or Very High (omit to keep current)")]
        string? confidence = null,
        [Description("Update access date as text (optional). Empty string clears. Formats: get_date_input_guide().")]
        string? date = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Update citation text")]
        string? text = null,
        [Description("Replace media handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes (omit to keep; [] clears)")]
        GrampsAttribute[]? attributes = null,
        [Description("Update private flag")]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var citation = await client.GetOrNullIfNotFoundAsync<GrampsCitation>($"/api/citations/{handle}");
            if (citation == null)
                return $"Citation not found: {handle}";

            var finalConfidence = Math.Clamp(
                CitationConfidenceParser.ParseOptional(confidence) ?? citation.Confidence,
                0,
                4);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder)
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
                    ? GrampsRequestMapping.ToAttributeRequests(attributes)
                    : GrampsRequestMapping.ToAttributeRequests(citation.AttributeList),
                NoteList = (string[]?)noteHandles ?? citation.NoteList,
                TagList = (string[]?)tagHandles ?? citation.TagList,
                Private = isPrivate ?? citation.Private
            };

            var response = await client.PutMutationAsync<GrampsCitation>($"/api/citations/{handle}", updateRequest, "Citation");
            return $"Citation updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a citation. Will warn if referenced by people, events or places.")]
    public static async Task<string> DeleteCitation(
        [Description("Citation handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/citations/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Citation not found: {handle}";
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
            return $"Citation deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
