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
        "Read-only: one citation (source title/handle, page, confidence, date of the cited entry). " +
        "Citations connect sources to facts on people, events, places, etc.")]
    internal static async Task<string> ReadCitationAsync(
        [Description("Citation handle. " + ToolDescriptionFragments.HandleDiscovery + " For one source's citations use list_records('citations', sourceHandle: ...).")]
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

    /// <summary>The checked request body of a new citation for create_records.</summary>
    internal static async Task<CreateCitationRequest> BuildCitationRequestAsync(
        string sourceHandle, FlexibleString? page, string confidence, string? date,
        FlexibleHandleList? noteHandles, FlexibleMediaLinkList? mediaHandles, FlexibleHandleList? tagHandles,
        FlexibleAttributeList? attributes, bool isPrivate, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(sourceHandle))
            throw McpToolErrors.ValidationError("Error: sourceHandle is required");
        if (GrampsRequestMapping.SourceAttributeError(attributes) is { } attributeError)
            throw McpToolErrors.ValidationError(attributeError);
        await KnownTypes.CheckSourceAttributesAsync(attributes, client);
        await KnownTypes.CheckMediaLinksAsync(mediaHandles, client);

        var confidenceLevel = Math.Clamp(CitationConfidenceParser.ParseRequired(confidence), 0, 4);

        var dateRequest = AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range);
        await new LinkIds()
            .One("sourceHandle", sourceHandle, "sources", h => sourceHandle = h)
            .Handles("noteHandles", noteHandles, "notes")
            .MediaLinks("mediaHandles", mediaHandles)
            .Handles("tagHandles", tagHandles, "tags")
            .ResolveAsync(client);

        return new CreateCitationRequest
        {
            Source = sourceHandle,
            Page = page,
            Confidence = confidenceLevel,
            Date = dateRequest,
            MediaList = GrampsRequestMapping.ToMediaRefRequests(mediaHandles),
            NoteList = noteHandles,
            TagList = tagHandles,
            AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
            Private = isPrivate
        };
    }

    [McpServerTool(Title = "Update Citation", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing citation's source, page, date, confidence, attributes or links to notes, media and tags. " +
        "Which people, events and other records it supports is stored on them: attach or detach it with their update tool " +
        "(citationHandles), not here. A change shows on every record that cites it: to cite another page or entry " +
        "for one record, create a new citation with create_records; to remove one, delete_records. " +
        "sourceHandle moves the citation to another source, an empty string clears date, and attributes replace the whole " +
        "list whatever linkMode says. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing citation returns a not-found message. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
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
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleMediaLinkList.DescriptionHint + " " + FlexibleMediaLinkList.UpdateHint)]
        FlexibleMediaLinkList? mediaHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.TagHint)]
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
            await KnownTypes.CheckMediaLinksAsync(mediaHandles, client, linkMode);
            await new LinkIds()
                .One("sourceHandle", sourceHandle, "sources", h => sourceHandle = h)
                .Handles("noteHandles", noteHandles, "notes")
                .MediaLinks("mediaHandles", mediaHandles)
                .Handles("tagHandles", tagHandles, "tags")
                .ResolveAsync(client, "Nothing was changed.");
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "citations");
            var citation = await GrampsObjectPatch.LoadAsync(client, $"/api/citations/{Uri.EscapeDataString(resolvedHandle)}");
            if (citation == null)
                return NotFoundHelper.NotFoundMessage("Citation", handle);

            citation.Set("source_handle", sourceHandle);
            citation.Set("page", (string?)page);
            if (CitationConfidenceParser.ParseOptional(confidence) is { } confidenceLevel)
                citation.Set("confidence", Math.Clamp(confidenceLevel, 0, 4));
            if (date != null)
                citation.SetOrRemove("date", AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range));
            citation.ApplyMediaLinks(mediaHandles, linkMode);
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
