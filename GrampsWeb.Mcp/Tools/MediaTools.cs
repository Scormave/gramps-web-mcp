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
        "Get media metadata by handle. Returns file path, MIME type, checksum, and description. " +
        "Media objects reference images, audio, video and other files attached to genealogical data.")]
    public static async Task<string> GetMedia(
        [Description("Media handle — use list_objects('media') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{handle}");
            return media == null
                ? $"Media not found: {handle}"
                : MediaFormatter.FormatMediaFull(media);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update media metadata (description, date, notes, tags). " +
        "Note: binary file upload is not supported via MCP. " +
        "Access date strings: get_date_input_guide(). Attributes: get_structured_field_input_guide().")]
    public static async Task<string> UpdateMedia(
        [Description("Media handle")]
        string handle,
        [Description("Update description")]
        string? description = null,
        [Description("Update access date as text (optional). Empty string clears. Formats: get_date_input_guide().")]
        string? date = null,
        [Description("How to read numeric slash/dot dates; see get_date_input_guide()")]
        DateComponentOrder dateComponentOrder = DateComponentOrder.Iso,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tag handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace citation handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? citationHandles = null,
        [Description("Replace attributes (omit to keep; [] clears). " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Update private flag")]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var media = await client.GetOrNullIfNotFoundAsync<GrampsMedia>($"/api/media/{handle}");
            if (media == null)
                return $"Media not found: {handle}";

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
            return $"Media updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Description: {response.Description ?? "—"}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a media object. Removes database entry, not the physical file. " +
        "Will warn if referenced by people, events, places, or sources.")]
    public static async Task<string> DeleteMedia(
        [Description("Media handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/media/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Media not found: {handle}";
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
            return $"Media deleted successfully [{handle}]\nNote: database entry removed, physical file remains.";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
