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
    [Description(
        "Read-only: one citation (source title/handle, page, confidence, access date). " +
        "Citations connect sources to facts on people, events, places, etc.")]
    internal static async Task<string> ReadCitationAsync(
        [Description("Citation handle. " + ToolDescriptionFragments.HandleDiscovery + " For one source's citations use list_objects('citations', sourceHandle: ...).")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "citations");
            var citation = await client.GetOrNullIfNotFoundAsync<GrampsCitation>(
                $"/api/citations/{Uri.EscapeDataString(resolvedHandle)}");
            if (citation == null)
                return NotFoundHelper.NotFoundMessage("Citation", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "citations", resolvedHandle);
            return await CitationFormatter.FormatCitationFull(citation, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Citation", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a citation: one specific spot in an existing source (page, act or entry number) with its date, confidence, " +
        "notes and media such as the scan. Returns the new handle, Gramps ID and page, with next steps. " +
        "The source must already exist (search, or create_source). The citation supports nothing until it is attached: " +
        "add it to each person, family, event or place it names (citationHandles, linkMode add, on their update tool). " +
        "Does not check for duplicates; change an existing citation with update_citation. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateCitation(
        [Description("The source being cited (required). " + ToolDescriptionFragments.HandleDiscovery)]
        string sourceHandle,
        [Description("Where in the source: page, act or entry number (optional). " + FlexibleString.DescriptionHint)]
        FlexibleString? page = null,
        [Description("How reliable the cited record is: Very Low, Low, Normal (default), High or Very High.")]
        string confidence = "Normal",
        [Description("Date of the cited entry, optional. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Notes, e.g. a transcript of the entry (create_note with type Transcript). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Media such as the scan of the page. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint + " " + FlexibleAttributeList.SourceAttributesHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark the citation private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceHandle))
                throw McpToolErrors.ValidationError("Error: sourceHandle is required");
            if (GrampsRequestMapping.SourceAttributeError(attributes) is { } attributeError)
                throw McpToolErrors.ValidationError(attributeError);
            await KnownTypes.CheckSourceAttributesAsync(attributes, client);
            var resolvedSourceHandle = await HandleResolver.ResolveToHandleAsync(sourceHandle, client, "sources");

            var confidenceLevel = Math.Clamp(CitationConfidenceParser.ParseRequired(confidence), 0, 4);

            var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);

            var request = new CreateCitationRequest
            {
                Source = resolvedSourceHandle,
                Page = page,
                Confidence = confidenceLevel,
                Date = dateRequest,
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/citations/", request, "Citation");
            return ResponseEnvelope.CreateSuccess("Citation", handle, grampsId,
                page, ResponseEnvelope.CitationCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Citation", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing citation: its source, page, confidence, date, notes, media, tags, attributes or the private flag. " +
        ToolDescriptionFragments.UpdateSemantics + " Returns the handle and Gramps ID; a missing citation returns a not-found message. " +
        "Which people and events it supports is stored on them (their citationHandles), not here. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateCitation(
        [Description("The citation to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Another source to cite instead. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.HandleDiscovery)]
        string? sourceHandle = null,
        [Description("New page, act or entry number. " + ToolDescriptionFragments.OmitToKeepScalar + " " + FlexibleString.DescriptionHint)]
        FlexibleString? page = null,
        [Description("New confidence: Very Low, Low, Normal, High or Very High. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? confidence = null,
        [Description("New date. Omit to keep the current date; pass an empty string to remove it. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint + " " +
                     FlexibleAttributeList.SourceAttributesHint)]
        FlexibleAttributeList? attributes = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            if (GrampsRequestMapping.SourceAttributeError(attributes) is { } attributeError)
                throw McpToolErrors.ValidationError(attributeError);
            await KnownTypes.CheckSourceAttributesAsync(attributes, client);
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "citations");
            var resolvedSourceHandle = sourceHandle is null
                ? null
                : await HandleResolver.ResolveToHandleAsync(sourceHandle, client, "sources");
            var citation = await GrampsObjectPatch.LoadAsync(client, $"/api/citations/{Uri.EscapeDataString(resolvedHandle)}");
            if (citation == null)
                return NotFoundHelper.NotFoundMessage("Citation", handle);

            citation.Set("source_handle", resolvedSourceHandle);
            citation.Set("page", (string?)page);
            if (CitationConfidenceParser.ParseOptional(confidence) is { } confidenceLevel)
                citation.Set("confidence", Math.Clamp(confidenceLevel, 0, 4));
            if (date != null)
                citation.SetOrRemove("date", AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range));
            citation.ApplyMediaHandles(mediaHandles, linkMode);
            citation.ReplaceAttributes(attributes);
            citation.ApplyHandles("note_list", noteHandles, linkMode);
            citation.ApplyHandles("tag_list", tagHandles, linkMode);
            citation.Set("private", isPrivate);

            await citation.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Citation", citation.Handle, citation.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
