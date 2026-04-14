using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Media objects—images, audio, and other digital files.
/// </summary>
[McpServerToolType]
public static class MediaTools
{
    [McpServerTool]
    [Description(
        "Read-only: media object metadata (path, MIME, checksum, description). " +
        "Does not upload or download file bytes via MCP.")]
    public static async Task<string> GetMedia(
        [Description("Media handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{handle}");
            return media == null
                ? NotFoundHelper.NotFoundMessage("Media", handle)
                : MediaFormatter.FormatMediaFull(media);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
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
        [Description("Ambiguous numeric date order. " + ToolDescriptionFragments.CallGetDateInputGuide)]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
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
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{handle}");
            if (media == null)
                return NotFoundHelper.NotFoundMessage("Media", handle);

            var dateRequest = date != null
                ? AgentDateParser.ToDateRequestOrNull(date, dateComponentOrder)
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

            var response = await client.PutMutationAsync<GrampsMedia>($"/api/media/{handle}", updateRequest, "Media");
            return ResponseEnvelope.UpdateSuccess("Media", response.Handle, response.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
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
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/media/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return NotFoundHelper.NotFoundMessage("Media", handle);
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
                return $"⚠️ Cannot delete media [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_media(handle, force=true).\n" +
                       $"Note: database entry will be removed, but the physical file remains.";
            }

            await client.DeleteAsync($"/api/media/{handle}");
            return ResponseEnvelope.DeleteSuccess("Media", handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
