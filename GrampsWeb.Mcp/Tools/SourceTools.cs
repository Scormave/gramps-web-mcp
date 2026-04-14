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
        "Read-only: one source (title, author, publication, abbreviation, repository refs). " +
        "Sources are what citations point at.")]
    public static async Task<string> GetSource(
        [Description("Source handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>($"/api/sources/{handle}");
            return source == null
                ? NotFoundHelper.NotFoundMessage("Source", handle)
                : SourceFormatter.FormatSourceFull(source);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a source (write). Create sources before citations. " +
        "Optional repositoryHandles link to where the item is held. " +
        ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> CreateSource(
        [Description("Title (required).")]
        string title,
        [Description("Author name (optional)")]
        string? author = null,
        [Description("Publication info (optional)")]
        string? pubinfo = null,
        [Description("Abbreviation (optional)")]
        string? abbrev = null,
        [Description("Repository handles. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? repositoryHandles = null,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Media handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description(FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
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
                MediaList = mediaHandles,
                NoteList = noteHandles,
                TagList = tagHandles,
                AttributeList = GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes),
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsSource>("/api/sources/", request, "Source");
            return ResponseEnvelope.CreateSuccess("Source", response.Handle, response.GrampsId,
                response.Title, ResponseEnvelope.SourceCreateNextSteps(response.Handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update a source (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetStructuredFieldInputGuide)]
    public static async Task<string> UpdateSource(
        [Description("Source handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Title. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? title = null,
        [Description("Author. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? author = null,
        [Description("Publication info. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? pubinfo = null,
        [Description("Abbreviation. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? abbrev = null,
        [Description("Repository refs. Omit to keep. Non-empty replaces the list; empty array does not clear (omit to keep). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? repositoryHandles = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace media. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? mediaHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Replace attributes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleAttributeList.DescriptionHint)]
        FlexibleAttributeList? attributes = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var source = await client.GetOrNullIfNotFoundAsync<GrampsSource>($"/api/sources/{handle}");
            if (source == null)
                return NotFoundHelper.NotFoundMessage("Source", handle);

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
                MediaList = (string[]?)mediaHandles ?? source.MediaList,
                RepositoryRefList = repoRefList ?? ToRepositoryRefRequestObjects(source.RepositoryRefList),
                AttributeList = attributes != null
                    ? GrampsRequestMapping.ToAttributeRequests((GrampsAttribute[]?)attributes)
                    : GrampsRequestMapping.ToAttributeRequests(source.AttributeList),
                NoteList = (string[]?)noteHandles ?? source.NoteList,
                TagList = (string[]?)tagHandles ?? source.TagList,
                Private = isPrivate ?? source.Private
            };

            var response = await client.PutMutationAsync<GrampsSource>($"/api/sources/{handle}", updateRequest, "Source");
            return ResponseEnvelope.UpdateSuccess("Source", response.Handle, response.GrampsId);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a source (destructive). WARNING: citations pointing at this source break or lose the link. " +
        "Blocked when backlinks exist unless force=true.")]
    public static async Task<string> DeleteSource(
        [Description("Source handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite citations still referencing it (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/sources/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return NotFoundHelper.NotFoundMessage("Source", handle);
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
            return ResponseEnvelope.DeleteSuccess("Source", handle);
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
