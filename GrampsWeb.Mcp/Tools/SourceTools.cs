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

    /// <summary>The checked request body of a new source for create_records.</summary>
    internal static async Task<CreateSourceRequest> BuildSourceRequestAsync(
        string title, string? author, string? pubinfo, string? abbrev, FlexibleRepositoryRefList? repositoryHandles,
        FlexibleHandleList? noteHandles, FlexibleMediaLinkList? mediaHandles, FlexibleHandleList? tagHandles,
        FlexibleAttributeList? attributes, bool isPrivate, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw McpToolErrors.ValidationError("Error: title is required");
        if (GrampsRequestMapping.SourceAttributeError(attributes) is { } attributeError)
            throw McpToolErrors.ValidationError(attributeError);
        await KnownTypes.CheckSourceAttributesAsync(attributes, client);
        await KnownTypes.CheckMediaLinksAsync(mediaHandles, client);

        var repoRefList = (GrampsRepositoryRef[]?)repositoryHandles;
        await KnownTypes.CheckRepositoryRefsAsync(repoRefList, client);
        await new LinkIds()
            .RepositoryRefs("repositoryHandles", repoRefList)
            .Handles("noteHandles", noteHandles, "notes")
            .MediaLinks("mediaHandles", mediaHandles)
            .Handles("tagHandles", tagHandles, "tags")
            .ResolveAsync(client);

        return new CreateSourceRequest
        {
            Title = title,
            Author = author,
            PubInfo = pubinfo,
            Abbrev = abbrev,
            RepositoryRefList = repoRefList,
            MediaList = GrampsRequestMapping.ToMediaRefRequests(mediaHandles),
            NoteList = noteHandles,
            TagList = tagHandles,
            AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
            Private = isPrivate
        };
    }

    [McpServerTool(Title = "Update Source", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing source's title, author, publication info, abbreviation, attributes or links to repositories, " +
        "notes, media and tags. For a new source use create_records; to remove one, delete_records; a page or act inside " +
        "the source is a citation, changed with update_citation. repositoryHandles carry each repository's call number, " +
        "so pass a linked repository with a new call number to change it; attributes replace the whole list whatever " +
        "linkMode says. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing source returns a not-found message. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
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
        string linkMode = "add")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            if (GrampsRequestMapping.SourceAttributeError(attributes) is { } attributeError)
                throw McpToolErrors.ValidationError(attributeError);
            await KnownTypes.CheckSourceAttributesAsync(attributes, client);
            await KnownTypes.CheckMediaLinksAsync(mediaHandles, client, linkMode);
            await KnownTypes.CheckRepositoryRefsAsync(repositoryHandles, client, linkMode);
            await new LinkIds()
                .RepositoryRefs("repositoryHandles", repositoryHandles)
                .Handles("noteHandles", noteHandles, "notes")
                .MediaLinks("mediaHandles", mediaHandles)
                .Handles("tagHandles", tagHandles, "tags")
                .ResolveAsync(client, "Nothing was changed.");
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "sources");
            var source = await GrampsObjectPatch.LoadAsync(client, $"/api/sources/{Uri.EscapeDataString(resolvedHandle)}");
            if (source == null)
                return NotFoundHelper.NotFoundMessage("Source", handle);

            source.Set("title", title);
            source.Set("author", author);
            source.Set("pubinfo", pubinfo);
            source.Set("abbrev", abbrev);
            source.ApplyRefs("reporef_list", ((GrampsRepositoryRef[]?)repositoryHandles)?.Select(r => new GrampsRepositoryRef
            {
                Ref = r.Ref?.Trim(),
                CallNumber = r.CallNumber?.Trim(),
                MediaType = r.MediaType?.Trim(),
                Private = r.Private,
                NoteList = r.NoteList
            }).ToArray(), linkMode);
            source.ApplyMediaLinks(mediaHandles, linkMode);
            source.ReplaceAttributes(attributes);
            source.ApplyHandles("note_list", noteHandles, linkMode);
            source.ApplyHandles("tag_list", tagHandles, linkMode);
            source.Set("private", isPrivate);

            await source.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Source", source.Handle, source.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
