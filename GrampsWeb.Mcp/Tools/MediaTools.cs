using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Media objects—images, audio, and other digital files.
/// </summary>
[McpServerToolType]
public static class MediaTools
{
    [McpServerTool(Title = "Get Media", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: media object metadata (path, MIME, checksum, description). " +
        "For Open WebUI vision access, use GetMediaThumbnail or GetMediaFile. " +
        "Full MCP clients may also read resources gramps://media/{handle}/thumbnail/{size} or gramps://media/{handle}/file.")]
    public static async Task<string> GetMedia(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(
                $"/api/media/{Uri.EscapeDataString(resolvedHandle)}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "media", resolvedHandle);
            return MediaFormatter.FormatMediaFull(media, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Get Media Thumbnail", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: download a media thumbnail as MCP image content for vision-capable tool clients such as Open WebUI. " +
        "Preferred before requesting the full media file. Requires GRAMPS_MEDIA_RESOURCES_ENABLED=true and respects media size, MIME, and private-record safeguards.")]
    public static async Task<ImageContentBlock> GetMediaThumbnail(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Thumbnail size in pixels. Must be positive. Default 256.")]
        int size = 256,
        GrampsApiClient client = null!,
        GrampsConfig config = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var thumbnail = await GrampsResources.DownloadMediaThumbnailAsync(resolvedHandle, size, client, config);
            GrampsResources.EnsureImageMime(thumbnail.MimeType);
            return ImageContentBlock.FromBytes(thumbnail.Binary.Bytes, thumbnail.MimeType);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Get Media File", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: download a full media file as MCP image content for vision-capable tool clients such as Open WebUI. " +
        "Use only when a thumbnail is insufficient. Rejects non-image media; PDFs and other document files remain available only through MCP resources.")]
    public static async Task<ImageContentBlock> GetMediaFile(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client,
        GrampsConfig config)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var mediaFile = await GrampsResources.DownloadMediaFileAsync(resolvedHandle, client, config);
            GrampsResources.EnsureImageMime(mediaFile.MimeType);
            return ImageContentBlock.FromBytes(mediaFile.Binary.Bytes, mediaFile.MimeType);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Media", ReadOnly = false, Destructive = false)]
    [Description(
        "Update media metadata (write). Binary upload is not supported here—only fields stored on the media record. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetDateInputGuide + " " + ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdateMedia(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Description. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? description = null,
        [Description("Date text. Omit to keep. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace citations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(
                $"/api/media/{Uri.EscapeDataString(resolvedHandle)}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso)
                : GrampsRequestMapping.ToDateRequestOrNull(media.Date);

            var updateRequest = new CreateMediaRequest
            {
                Class = "Media",
                Handle = media.Handle,
                GrampsId = media.GrampsId,
                Change = media.Change,
                Path = media.Path,
                Mime = media.Mime,
                Description = description ?? media.Description,
                Date = dateRequest,
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(media.AttributeList),
                CitationList = (string[]?)citationHandles ?? media.CitationList,
                NoteList = (string[]?)noteHandles ?? media.NoteList,
                TagList = (string[]?)tagHandles ?? media.TagList,
                Private = isPrivate ?? media.Private
            };

            await client.PutMutationAsync($"/api/media/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Media", media.Handle, media.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Delete Media", ReadOnly = false, Destructive = true)]
    [Description(
        "Delete a media record (destructive). Removes the Gramps object, not necessarily the file on disk. " +
        "Blocked when other objects reference it unless force=true.")]
    public static async Task<string> DeleteMedia(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite backlinks (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Media", "media", resolvedHandle, force, handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
