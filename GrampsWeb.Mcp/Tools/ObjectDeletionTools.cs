using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool for deleting any supported Gramps object with backlink protection.
/// </summary>
[McpServerToolType]
public static class ObjectDeletionTools
{
    private sealed record ObjectKind(string DisplayName, string ApiPath);

    private static readonly IReadOnlyDictionary<string, ObjectKind> ObjectKinds =
        new Dictionary<string, ObjectKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = new("Person", "people"),
            ["family"] = new("Family", "families"),
            ["event"] = new("Event", "events"),
            ["place"] = new("Place", "places"),
            ["source"] = new("Source", "sources"),
            ["citation"] = new("Citation", "citations"),
            ["note"] = new("Note", "notes"),
            ["media"] = new("Media", "media"),
            ["repository"] = new("Repository", "repositories"),
            ["tag"] = new("Tag", "tags")
        };

    [McpServerTool(Title = "Delete Object", ReadOnly = false, Destructive = true)]
    [Description(
        "Delete one Gramps object (destructive). Select its singular objectType: person, family, event, place, source, citation, note, media, repository, or tag. " +
        "The server checks backlinks and blocks deletion by default. Pass force=true only when you accept that remaining records may have dangling references.")]
    public static async Task<string> DeleteObject(
        [Description("Object type: person | family | event | place | source | citation | note | media | repository | tag.")]
        string objectType,
        [Description("Object handle or Gramps ID (for example I0001). Handles are opaque API strings; Gramps IDs are resolved automatically.")]
        string handle,
        [Description("If true, delete despite backlinks. This may leave dangling references. Default false.")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (!ObjectKinds.TryGetValue(objectType, out var kind))
                throw McpToolErrors.ValidationError(
                    "Invalid objectType. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, kind.ApiPath);
            return await DeleteHelper.DeleteWithBacklinksAsync(
                client, kind.DisplayName, kind.ApiPath, resolvedHandle, force, handle);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
