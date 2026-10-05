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
/// MCP tools for reading Source objects—the scholarly sources that citations reference.
/// </summary>
[McpServerToolType]
public static class SourceTools
{
    [Description(
        "Read-only: one source (title, author, publication, abbreviation, repository refs). " +
        "Sources are what citations point at.")]
    internal static async Task<string> ReadSourceAsync(
        [Description("Source handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "sources");
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>(
                $"/api/sources/{Uri.EscapeDataString(resolvedHandle)}");
            if (source == null)
                return NotFoundHelper.NotFoundMessage("Source", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "sources", resolvedHandle);
            return await SourceFormatter.FormatSourceFull(source, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Source", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a source: a book, register, archive unit or website that facts come from, with title, author, publication info " +
        "and the repositories that hold it (with call numbers). Returns the new handle, Gramps ID and title, with next steps. " +
        "Does not check for duplicates: search for the source first. A source alone proves nothing: cite a page or act " +
        "of it with create_citation, then attach that citation to people and events. Change an existing source with update_source. " +
        ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateSource(
        [Description("Title (required).")]
        string title,
        [Description("Author or issuing body (optional).")]
        string? author = null,
        [Description("Publication info: publisher, place, year (optional).")]
        string? pubinfo = null,
        [Description("Short title for lists (optional).")]
        string? abbrev = null,
        [Description("Repositories that hold the source, each with an optional call number and media type; the repositories must exist (create_repository). " + FlexibleRepositoryRefList.DescriptionHint)]
        FlexibleRepositoryRefList? repositoryHandles = null,
        [Description("Notes. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Media. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tags. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark the source private (default false).")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
                throw McpToolErrors.ValidationError("Error: title is required");

            var repoRefList = (GrampsRepositoryRef[]?)repositoryHandles;

            var request = new CreateSourceRequest
            {
                Title = title,
                Author = author,
                PubInfo = pubinfo,
                Abbrev = abbrev,
                RepositoryRefList = repoRefList,
                MediaList = GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles),
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate
            };

            var (handle, grampsId) = await client.PostMutationAsync("/api/sources/", request, "Source");
            return ResponseEnvelope.CreateSuccess("Source", handle, grampsId,
                title, ResponseEnvelope.SourceCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Source", ReadOnly = false, Destructive = false)]
    [Description(
        "Change an existing source: title, author, publication info, abbreviation, repositories, notes, media, tags, " +
        "attributes or the private flag. " + ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing source returns a not-found message. " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateSource(
        [Description("The source to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New title. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? title = null,
        [Description("New author. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? author = null,
        [Description("New publication info. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? pubinfo = null,
        [Description("New abbreviation. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? abbrev = null,
        [Description("Repositories that hold the source. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleRepositoryRefList.DescriptionHint)]
        FlexibleRepositoryRefList? repositoryHandles = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Media. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Attributes. " + ToolDescriptionFragments.ReplacedListOnUpdate + " " + FlexibleAttributeList.DescriptionHint)]
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
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "sources");
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>(
                $"/api/sources/{Uri.EscapeDataString(resolvedHandle)}");
            if (source == null)
                return NotFoundHelper.NotFoundMessage("Source", handle);

            var repoHandlesUpdate = (GrampsRepositoryRef[]?)repositoryHandles;

            var updateRequest = new CreateSourceRequest
            {
                Class = "Source",
                Handle = source.Handle,
                GrampsId = source.GrampsId,
                Change = source.Change,
                Title = title ?? source.Title,
                Author = author ?? source.Author,
                PubInfo = pubinfo ?? source.PubInfo,
                Abbrev = abbrev ?? source.Abbrev,
                MediaList = LinkUpdates.Apply(GrampsRequestMapping.ToMediaRefRequests(source.MediaList),
                    mediaHandles is null ? null : (GrampsRequestMapping.ToMediaRefRequests((string[]?)mediaHandles, source.MediaList) ?? []), linkMode, x => x.Ref),
                RepositoryRefList = LinkUpdates.Apply(GrampsRequestMapping.ToRepositoryRefRequests(source.RepositoryRefList),
                    repositoryHandles is null ? null : (GrampsRequestMapping.ToRepositoryRefRequests(repoHandlesUpdate, source.RepositoryRefList) ?? []), linkMode, x => x.Ref),
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(source.AttributeList),
                NoteList = LinkUpdates.Apply(source.NoteList, (string[]?)noteHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(source.TagList, (string[]?)tagHandles, linkMode, x => x),
                Private = isPrivate ?? source.Private
            };

            await client.PutMutationAsync($"/api/sources/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Source", source.Handle, source.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
