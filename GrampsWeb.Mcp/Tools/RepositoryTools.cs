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
/// MCP tools for reading Repository objects—archives, libraries, and collections.
/// </summary>
[McpServerToolType]
public static class RepositoryTools
{
    [McpServerTool]
    [Description(
        "Get repository data by handle. Returns name, type (Archive/Library/Website/etc), " +
        "address information and URLs where sources can be accessed.")]
    public static async Task<string> GetRepository(
        [Description("Repository handle — use list_objects('repositories') or search() to find handles")]
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
        "Create an archive, library or repository record. " +
        "Call get_types() for valid repository_type values.")]
    public static async Task<string> CreateRepository(
        [Description("Repository name")]
        string name,
        [Description("Repository type — call get_types to get valid values")]
        string? repoType = null,
        [Description("Street address (optional)")]
        string? address = null,
        [Description("Website URL (optional)")]
        string? url = null,
        [Description("Array of note handles (optional)")]
        string[]? noteHandles = null,
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
                NoteList = noteHandles
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
        "Update existing repository. Pass only fields that need to change. " +
        "⚠ WARNING: passing empty lists will REMOVE those linked objects.")]
    public static async Task<string> UpdateRepository(
        [Description("Repository handle")]
        string handle,
        [Description("Update repository name")]
        string? name = null,
        [Description("Update repository type")]
        string? repoType = null,
        [Description("Update address")]
        string? address = null,
        [Description("Update URL")]
        string? url = null,
        [Description("Replace note handles")]
        string[]? noteHandles = null,
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
                AddressList = repo.AddressList,
                UrlList = repo.UrlList,
                NoteList = noteHandles ?? repo.NoteList,
                TagList = repo.TagList,
                Private = repo.Private
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
        "Delete a repository. Will warn if referenced by sources.")]
    public static async Task<string> DeleteRepository(
        [Description("Repository handle")]
        string handle,
        [Description("Force delete despite backlinks (default: false)")]
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
}
