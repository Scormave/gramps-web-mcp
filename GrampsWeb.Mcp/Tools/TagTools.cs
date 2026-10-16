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
    [Description(
        "Read-only: one tag (name, color hex, priority). Tags label any object type.")]
    internal static async Task<string> ReadTagAsync(
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

    [McpServerTool(Title = "Create Tag", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Create a tag: a colored label such as \"To check\" or \"Line of John Smith\" for grouping records. " +
        "Returns the new handle, Gramps ID and name, with next steps. Does not check for duplicate names: " +
        "look through list_objects(objectType: \"tags\") first. The tag labels nothing yet: add its handle to the " +
        "tagHandles of each record (their create or update tool, linkMode add on update). Rename or recolor it later " +
        "with update_tag. " + ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> CreateTag(
        [Description("Tag name shown on records (required).")]
        string name,
        [Description("Color as six hex digits RRGGBB, with or without #, e.g. FF5733; stored as #ff5733. Default 000000 (black).")]
        string color = "000000",
        [Description("Sort priority among tags in Gramps (default 0).")]
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
                Color = NormalizeColor(color),
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

    [McpServerTool(Title = "Update Tag", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Rename a tag or change its color or priority; every record carrying the tag shows the change. " +
        "For a new tag use create_tag; to remove one, delete_object. Which records carry a tag is stored on the " +
        "records: change that through their tagHandles (their update tool, linkMode add or remove), not here. " +
        "Pass only what changes; omitted arguments stay as they are. A name another tag already uses is rejected, " +
        "because Gramps filters find tags by name. Returns the stored name, color and priority; a missing tag " +
        "returns a not-found message. " + ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> UpdateTag(
        [Description("The tag to change: its handle or exact name. Tags have no Gramps ID; " +
                     "find them with list_objects(objectType: \"tags\").")]
        string handle,
        [Description("New name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("New color as six hex digits RRGGBB, with or without #, e.g. FF5733; stored as #ff5733. " +
                     ToolDescriptionFragments.OmitToKeepScalar)]
        string? color = null,
        [Description("New sort priority among tags in Gramps. " + ToolDescriptionFragments.OmitToKeepScalar)]
        int? priority = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (name != null && string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name cannot be empty; omit it to keep the current name");
            var storedColor = color == null ? null : NormalizeColor(color);

            using var updateLease = await client.BeginUpdateAsync();
            var tags = (await client.GetPagedListAsync<GrampsTag>("/api/tags/")).Objects ?? [];
            var target = FindTag(tags, handle);
            if (target?.Handle == null)
                return NotFoundHelper.NotFoundMessage("Tag", handle);

            if (name != null)
            {
                var taken = tags.FirstOrDefault(other => other.Handle != target.Handle
                    && string.Equals(other.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (taken != null)
                    throw McpToolErrors.ValidationError(
                        $"Error: another tag is already named \"{taken.Name}\" (handle {taken.Handle}); " +
                        "choose another name, or tag the records with that one instead");
            }

            var tag = await GrampsObjectPatch.LoadAsync(client, $"/api/tags/{Uri.EscapeDataString(target.Handle)}");
            if (tag == null)
                return NotFoundHelper.NotFoundMessage("Tag", handle);

            tag.Set("name", name);
            tag.Set("color", storedColor);
            tag.Set("priority", priority);

            await tag.SaveAsync(client);
            return ResponseEnvelope.UpdateSuccess("Tag", tag.Handle, tag.GrampsId,
            [
                new("name", ResponseEnvelope.HeaderValue(GrampsObjectPatch.StringValue(tag.Root["name"]))),
                new("color", ResponseEnvelope.HeaderValue(GrampsObjectPatch.StringValue(tag.Root["color"]))),
                new("priority", tag.Root["priority"]?.ToJsonString() ?? "0")
            ]);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// The tag a caller names: by handle, then by exact name, then by the one name that matches ignoring case;
    /// <c>null</c> when none or several match.
    /// </summary>
    private static GrampsTag? FindTag(IReadOnlyList<GrampsTag> tags, string handleOrName)
    {
        var key = handleOrName.Trim();
        var byName = tags.Where(t => string.Equals(t.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase)).ToList();
        return tags.FirstOrDefault(t => t.Handle == key)
            ?? byName.FirstOrDefault(t => t.Name?.Trim() == key)
            ?? (byName.Count == 1 ? byName[0] : null);
    }

    /// <summary>
    /// A tag color as Gramps Web stores it, <c>#rrggbb</c>: Gramps Web and Gramps desktop read the color as CSS
    /// or GTK color text, which needs the <c>#</c>.
    /// </summary>
    private static string NormalizeColor(string color)
    {
        var hex = color.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length != 6 || !hex.All(char.IsAsciiHexDigit))
            throw McpToolErrors.ValidationError(
                $"Error: color must be six hex digits RRGGBB, e.g. FF5733; got \"{color}\"");
        return "#" + hex.ToLowerInvariant();
    }
}
