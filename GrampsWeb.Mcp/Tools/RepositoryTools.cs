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
/// MCP tools for reading Repository objects—archives, libraries, and collections.
/// </summary>
[McpServerToolType]
public static class RepositoryTools
{
    [McpServerTool]
    [Description(
        "Read-only: one repository (name, type, address, URLs). Repositories are where sources live.")]
    public static async Task<string> GetRepository(
        [Description("Repository handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var repo = await client.GetOrNullIfNotFoundAsync<GrampsRepository>($"/api/repositories/{handle}");
            return repo == null
                ? $"Repository not found: {handle}"
                : await RepositoryFormatter.FormatRepositoryFullAsync(repo, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Create a repository (write). Returns handle and Gramps ID. " +
        ToolDescriptionFragments.CallGetTypes)]
    public static async Task<string> CreateRepository(
        [Description("Name (required).")]
        string name,
        [Description("Repository type key. " + ToolDescriptionFragments.CallGetTypes)]
        string? repoType = null,
        [Description("Street address (optional)")]
        string? address = null,
        [Description("Website URL (optional)")]
        string? url = null,
        [Description("Note handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tag handles (optional). " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark record private (default: false)")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            var request = new CreateRepositoryRequest
            {
                Name = name,
                Type = repoType,
                AddressList = RepositoryAddressListFromStreet(address),
                UrlList = RepositoryUrlListFromPath(url),
                NoteList = noteHandles,
                TagList = tagHandles,
                Private = isPrivate
            };

            var response = await client.PostMutationAsync<GrampsRepository>("/api/repositories/", request, "Repository");
            var typeLabel = await GrampsDefaultTypeLabels.FormatRepositoryTypeAsync(client, response.Type);
            return $"Repository created successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Name: {response.Name}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Update a repository (write). Only pass fields to change. " +
        ToolDescriptionFragments.UpdateEmptyListRemovesLinks + " " +
        ToolDescriptionFragments.CallGetTypes)]
    public static async Task<string> UpdateRepository(
        [Description("Repository handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("Name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("Type. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.CallGetTypes)]
        string? repoType = null,
        [Description("Street line (replaces address list when set). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? address = null,
        [Description("Website URL (replaces url list when set). " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? url = null,
        [Description("Replace notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Replace tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var repo = await client.GetOrNullIfNotFoundAsync<GrampsRepository>($"/api/repositories/{handle}");
            if (repo == null)
                return $"Repository not found: {handle}";

            var updateRequest = new CreateRepositoryRequest
            {
                Class = "Repository",
                Handle = repo.Handle,
                GrampsId = repo.GrampsId,
                Change = repo.Change,
                Name = name ?? repo.Name,
                Type = repoType ?? repo.Type,
                EmailList = repo.EmailList,
                AddressList = address != null ? RepositoryAddressListFromStreet(address) : repo.AddressList,
                UrlList = url != null ? RepositoryUrlListFromPath(url) : repo.UrlList,
                NoteList = (string[]?)noteHandles ?? repo.NoteList,
                TagList = (string[]?)tagHandles ?? repo.TagList,
                Private = isPrivate ?? repo.Private
            };

            var response = await client.PutMutationAsync<GrampsRepository>($"/api/repositories/{handle}", updateRequest, "Repository");
            var typeLabel = await GrampsDefaultTypeLabels.FormatRepositoryTypeAsync(client, response.Type);
            return $"Repository updated successfully\n" +
                   $"Handle: {response.Handle}\n" +
                   $"Gramps ID: {response.GrampsId}\n" +
                   $"Type: {typeLabel}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Delete a repository (destructive). Blocked when sources still reference it unless force=true.")]
    public static async Task<string> DeleteRepository(
        [Description("Repository handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("If true, delete despite backlinks (default false).")]
        bool force = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/repositories/{handle}?backlinks=true");
            if (payload is null || payload.Value.ValueKind == JsonValueKind.Null)
                return $"Repository not found: {handle}";
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
                return $"⚠️ Cannot delete repository [{handle}] — it has references:\n" +
                       $"{backlinksInfo}" +
                       $"To delete anyway, call delete_repository(handle, force=true).";
            }

            await client.DeleteAsync($"/api/repositories/{handle}");
            return $"Repository deleted successfully [{handle}]";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>Single street line as Gramps address_list entry; null input omits the field on create.</summary>
    private static object[]? RepositoryAddressListFromStreet(string? street)
    {
        if (street == null)
            return null;
        if (string.IsNullOrWhiteSpace(street))
            return [];
        return new object[] { new GrampsAddress { Street = street.Trim() } };
    }

    /// <summary>Single URL as urls entry; null input omits on create.</summary>
    private static object[]? RepositoryUrlListFromPath(string? path)
    {
        if (path == null)
            return null;
        if (string.IsNullOrWhiteSpace(path))
            return [];
        return new object[] { new GrampsUrl { Path = path.Trim(), Type = "Web Home" } };
    }
}
