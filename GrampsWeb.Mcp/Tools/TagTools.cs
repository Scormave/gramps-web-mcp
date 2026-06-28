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
    [McpServerTool(Title = "Get Tag", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: one tag (name, color hex, priority). Tags label any object type.")]
    public static async Task<string> GetTag(
        [Description("Tag handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "tags");
            var tag = await client.GetOrNullIfNotFoundAsync<GrampsTag>(
                $"/api/tags/{Uri.EscapeDataString(resolvedHandle)}");
            return tag == null
                ? NotFoundHelper.NotFoundMessage("Tag", handle)
                : TagFormatter.FormatTagFull(tag);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Tag", ReadOnly = false, Destructive = false)]
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

            var (handle, grampsId) = await client.PostMutationAsync("/api/tags/", request, "Tag");
            return ResponseEnvelope.CreateSuccess("Tag", handle, grampsId,
                name, ResponseEnvelope.TagCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Delete Tag", ReadOnly = false, Destructive = true)]
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
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "tags");
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, "Tag", "tags", resolvedHandle, force, handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
