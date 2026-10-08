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
        "Download a media file's bytes so you can see it. Default mode thumbnail renders a JPEG preview (PNG when transparent) from an image original " +
        "as MCP image content, 1568 pixels on the long edge unless size is given; EXIF and other metadata are stripped. " +
        "Prefer it for reading photos and document scans. Use mode file only when the original is needed: returns image, audio, " +
        "or embedded blob resource content according to MIME type. " +
        "Requires GRAMPS_MEDIA_RESOURCES_ENABLED=true and respects size and private-record safeguards. " +
        "For the media record itself (path, description, links) use get_object; to change it use update_media.")]
    public static async Task<CallToolResult> ReadMedia(
        [Description("The media object to show. " + ToolDescriptionFragments.HandleDiscovery)]
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
        "Change an existing media record: description, date, attributes, notes, tags, citations or the private flag. " +
        "The file itself cannot be uploaded or replaced here; media objects are created in Gramps Web. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing media object returns a not-found message, and an unreadable date is rejected before anything is saved. " +
        "To show the media on a person, event or citation, add it to their mediaHandles (linkMode add). " +
        ToolDescriptionFragments.InputGuide)]
    public static async Task<string> UpdateMedia(
        [Description("The media object to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New description (caption). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? description = null,
        [Description("New date. Omit to keep the current date; pass an empty string to remove it. " + ToolDescriptionFragments.DateText)]
        string? date = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Citations. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
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
            await KnownTypes.CheckAttributesAsync(attributes, client);
            using var updateLease = await client.BeginUpdateAsync();
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "media");
            var media = await GrampsObjectPatch.LoadAsync(client, $"/api/media/{Uri.EscapeDataString(resolvedHandle)}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);

            media.Set("desc", description);
            if (date != null)
                media.SetOrRemove("date", AgentDateParser.ToDateRequestOrNull(date, DateComponentOrder.Iso, DateIntervalPreference.Range));
            media.ReplaceAttributes(attributes);
            media.ApplyHandles("citation_list", citationHandles, linkMode);
            media.ApplyHandles("note_list", noteHandles, linkMode);
            media.ApplyHandles("tag_list", tagHandles, linkMode);
            media.Set("private", isPrivate);

            await media.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Media", media.Handle, media.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
