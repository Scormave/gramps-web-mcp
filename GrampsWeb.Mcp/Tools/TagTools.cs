using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading Tag objects—colored labels that can be attached to any genealogical object.
/// </summary>
[McpServerToolType]
public static class TagTools
{
    [McpServerTool]
    [Description(
        "Read-only: one tag (name, color hex, priority). Tags label any object type.")]
    public static async Task<string> GetTag(
        [Description("Tag handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var tag = await client.GetOrNullIfNotFoundAsync<GrampsTag>($"/api/tags/{handle}");
            return tag == null
                ? $"Tag not found: {handle}"
                : TagFormatter.FormatTagFull(tag);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a tag (write). Returns handle and Gramps ID. " +
        "color is six hex digits without # (e.g. FF5733). " +
        "Call list_objects('tags') first to avoid duplicate names. " +
        "Attach to objects via that object's tagHandles on create/update.")]
    public static async Task<string> CreateTag(
        [Description("Display name (required).")]
        string name,
        [Description("Color as six hex digits RRGGBB without #. Default 000000 (black).")]
        string color = "000000",
        [Description("Sort priority; lower often sorts first (default 0).")]
        int priority = 0,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var request = new CreateTagRequest
            {
                Name = name,
                Color = color,
                Priority = priority
            };

            var response = await client.PostMutationAsync<GrampsTag>("/api/tags/", request, "Tag");
            return $"Tag created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Name: {response.Name}\n" +
                   $"Color: #{response.Color}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update a tag (write). Scalar fields: omit to leave unchanged (no list-clear semantics on this tool).")]
    public static async Task<string> UpdateTag(
        [Description("Tag handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("RRGGBB six hex digits without #. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? color = null,
        [Description("Priority. " + ToolDescriptionFragments.OmitToKeepScalar)]
        int? priority = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var tag = await client.GetOrNullIfNotFoundAsync<GrampsTag>($"/api/tags/{handle}");
            if (tag == null)
                return $"Tag not found: {handle}";

            var updateRequest = new CreateTagRequest
            {
                Class = "Tag",
                Handle = tag.Handle,
                GrampsId = tag.GrampsId,
                Change = tag.Change,
                Name = name ?? tag.Name,
                Color = color ?? tag.Color,
                Priority = priority ?? tag.Priority
            };

            var response = await client.PutMutationAsync<GrampsTag>($"/api/tags/{handle}", updateRequest, "Tag");
            return $"Tag updated successfully\n" +
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
        "Delete a tag (destructive). Blocked when objects still carry the tag unless force=true.")]
    public static async Task<string> DeleteTag(
        [Description("Tag handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite objects referencing the tag (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/tags/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Tag not found: {handle}";
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
                return $"⚠️ Cannot delete tag [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_tag(handle, force=true).";
            }

            await client.DeleteAsync($"/api/tags/{handle}");
            return $"Tag deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
