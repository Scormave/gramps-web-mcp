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
    [McpServerTool]
    [Description(
        "Get source data by handle. Returns title, author, publication info, abbreviation, " +
        "and linked repository handles. Sources are the scholarly works that citations cite.")]
    public static async Task<string> GetSource(
        [Description("Source handle — use list_objects('sources') or search() to find handles")]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>($"/api/sources/{handle}");
            return source == null
                ? $"Source not found: {handle}"
                : SourceFormatter.FormatSourceFull(source);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a source document. Usually created before citations. " +
        "Link to repository if the physical document is held there.")]
    public static async Task<string> CreateSource(
        [Description("Source title")]
        string title,
        [Description("Author name (optional)")]
        string? author = null,
        [Description("Publication info (optional)")]
        string? pubinfo = null,
        [Description("Abbreviation (optional)")]
        string? abbrev = null,
        [Description("Repository handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? repositoryHandles = null,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(title))
                throw McpToolErrors.ValidationError("Error: title is required");

            var repoHandlesArray = (string[]?)repositoryHandles;
            var repoRefList = repoHandlesArray?.Length > 0
                ? repoHandlesArray.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var request = new CreateSourceRequest
            {
                Title = title,
                Author = author,
                PubInfo = pubinfo,
                Abbrev = abbrev,
                RepositoryRefList = repoRefList,
                NoteList = noteHandles
            };

            var response = await client.PostMutationAsync<GrampsSource>("/api/sources/", request, "Source");
            return $"Source created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Title: {response.Title}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update existing source. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects.")]
    public static async Task<string> UpdateSource(
        [Description("Source handle")]
        string handle,
        [Description("Update title")]
        string? title = null,
        [Description("Update author")]
        string? author = null,
        [Description("Update publication info")]
        string? pubinfo = null,
        [Description("Update abbreviation")]
        string? abbrev = null,
        [Description("Replace repository handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? repositoryHandles = null,
        [Description("Replace note handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>($"/api/sources/{handle}");
            if (source == null)
                return $"Source not found: {handle}";

            var repoHandlesUpdate = (string[]?)repositoryHandles;
            var repoRefList = repoHandlesUpdate != null && repoHandlesUpdate.Length > 0
                ? repoHandlesUpdate.Select(h => new { @ref = h } as object).ToArray()
                : null;

            var updateRequest = new CreateSourceRequest
            {
                Class = "Source",
                Handle = source.Handle,
                GrampsId = source.GrampsId,
                Change = source.Change,
                Title = title ?? source.Title,
                Author = author ?? source.Author,
                PubInfo = pubinfo ?? source.PubInfo,
                Abbrev = abbrev ?? source.Abbrev,
                MediaList = source.MediaList,
                RepositoryRefList = repoRefList ?? ToRepositoryRefRequestObjects(source.RepositoryRefList),
                AttributeList = GrampsRequestMapping.ToAttributeRequests(source.AttributeList),
                NoteList = (string[]?)noteHandles ?? source.NoteList,
                TagList = source.TagList,
                Private = source.Private
            };

            var response = await client.PutMutationAsync<GrampsSource>($"/api/sources/{handle}", updateRequest, "Source");
            return $"Source updated successfully\n" +
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
        "Delete a source. WARNING: all citations referencing this source will lose their source link. " +
        "Will warn if source is referenced by citations.")]
    public static async Task<string> DeleteSource(
        [Description("Source handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/sources/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Source not found: {handle}";
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
                return $"⚠️ Cannot delete source [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_source(handle, force=true).\n" +
                       $"WARNING: all citations referencing this source will lose their source link.";
            }

            await client.DeleteAsync($"/api/sources/{handle}");
            return $"Source deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static object[]? ToRepositoryRefRequestObjects(GrampsRepositoryRef[]? list)
    {
        if (list == null)
            return null;
        return list.Select(r => (object)new { @ref = r.Ref }).ToArray();
    }
}
