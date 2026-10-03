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
    [Description(
        "Read-only: media object metadata (path, MIME, checksum, description). " +
        "For Open WebUI vision access, use read_media with mode thumbnail or file. " +
        "Full MCP clients may also read resources gramps://media/{handle}/thumbnail/{size} or gramps://media/{handle}/file.")]
    internal static async Task<string> ReadMediaAsync(
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
            return await MediaFormatter.FormatMediaFull(media, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Read Media", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: download media bytes. Default mode thumbnail renders a JPEG preview (PNG when transparent) from an image original " +
        "as MCP image content, 1568 pixels on the long edge unless size is given; EXIF and other metadata are stripped. " +
        "Prefer it for reading photos and document scans. Use mode file only when the original is needed: returns image, audio, " +
        "or embedded blob resource content according to MIME type. " +
        "Requires GRAMPS_MEDIA_RESOURCES_ENABLED=true and respects size and private-record safeguards. " +
        "Use get_object with objectType media for metadata.")]
    public static async Task<CallToolResult> ReadMedia(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Download mode: thumbnail | file. Default thumbnail. Use file only when a preview is insufficient.")]
        string mode = "thumbnail",
        [Description("Thumbnail long edge in pixels, 1 to 4096; default 1568, which keeps handwriting and small print legible. " +
                     "Use 512 or less for a quick look at photos. Images are never upscaled. Only valid for mode thumbnail; omit for mode file.")]
        int? size = null,
        GrampsApiClient client = null!,
        GrampsConfig config = null!)
    {
        try
        {
            GrampsResources.EnsureMediaResourcesEnabled(config);
            GrampsResources.EnsureMediaHandle(handle);
            var normalizedMode = mode?.Trim().ToLowerInvariant();
            if (normalizedMode is not ("thumbnail" or "file"))
                throw McpToolErrors.ValidationError("Invalid mode. Must be thumbnail or file.");
            if (normalizedMode == "file" && size.HasValue)
                throw McpToolErrors.ValidationError("size is only supported for mode thumbnail; omit it for mode file.");
            if (normalizedMode == "thumbnail" && size.HasValue)
                MediaPreviewRenderer.EnsureValidSize(size.Value);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            if (normalizedMode == "file")
            {
                var mediaFile = await GrampsResources.DownloadMediaFileAsync(resolvedHandle, client, config);
                return GrampsResources.ToMediaFileCallToolResult(mediaFile);
            }
            var thumbnail = await GrampsResources.RenderMediaThumbnailAsync(
                resolvedHandle, size ?? MediaPreviewRenderer.DefaultSize, client, config);
            return new CallToolResult { Content = [ImageContentBlock.FromBytes(thumbnail.Binary.Bytes, thumbnail.MimeType)] };
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
        [Description("Date text. Omit to keep the current date; pass an empty string to remove it. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        string? date = null,
        [Description("Linked notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Linked tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Linked citations. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>(
                $"/api/media/{Uri.EscapeDataString(resolvedHandle)}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range)
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
                CitationList = LinkUpdates.Apply(media.CitationList, (string[]?)citationHandles, linkMode, x => x),
                NoteList = LinkUpdates.Apply(media.NoteList, (string[]?)noteHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(media.TagList, (string[]?)tagHandles, linkMode, x => x),
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

}
