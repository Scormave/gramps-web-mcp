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
    [Description(
        "Read-only: one repository (name, type, address, URLs). Repositories are where sources live.")]
    internal static async Task<string> ReadRepositoryAsync(
        [Description("Repository handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        GrampsApiClient client)
    {
        try
        {
            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "repositories");
            var repo = await client.GetOrNullIfNotFoundAsync<GrampsRepository>(
                $"/api/repositories/{Uri.EscapeDataString(resolvedHandle)}");
            if (repo == null)
                return NotFoundHelper.NotFoundMessage("Repository", handle);
            var backlinks = await BacklinkCollector.CollectAsync(client, "repositories", resolvedHandle);
            return await RepositoryFormatter.FormatRepositoryFullAsync(repo, client, backlinks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Create Repository", ReadOnly = false, Destructive = false)]
    [Description(
        "Create a repository (write): an archive, library, church, website or other holder of sources. " +
        "Returns handle and Gramps ID. Check list_objects('repositories') first so one holder is not entered twice; " +
        "change an existing repository with update_repository. Link sources to it afterwards through repositoryHandles " +
        "on create_source or update_source, which also carry the call number. noteHandles and tagHandles only link " +
        "existing notes and tags (create_note, create_tag); nothing else is created or changed.")]
    public static async Task<string> CreateRepository(
        [Description("Name, e.g. \"State Archive in Warsaw\" (required).")]
        string name,
        [Description("Repository type, e.g. Archive, Library, Church, Web site (optional; Gramps stores Library when omitted). " +
                     ToolDescriptionFragments.CallGetTypes)]
        string? repoType = null,
        [Description("Postal address as one line, e.g. \"12 High Street, London\", stored as the street of the repository's address (optional).")]
        string? address = null,
        [Description("Website, e.g. https://archive.example.org, stored as the repository's web home link (optional).")]
        string? url = null,
        [Description("Existing notes to link (optional); create them first with create_note. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Existing tags to link (optional); create them first with create_tag. " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Mark the repository private (default: false); Gramps Web hides private records from users not allowed to see them.")]
        bool isPrivate = false,
        GrampsApiClient client = null!)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                throw McpToolErrors.ValidationError("Error: name is required");

            if (repoType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(repoType, "repository_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

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

            var (handle, grampsId) = await client.PostMutationAsync("/api/repositories/", request, "Repository");
            return ResponseEnvelope.CreateSuccess(
                "Repository", handle, grampsId,
                name, ResponseEnvelope.RepositoryCreateNextSteps(handle!));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Update Repository", ReadOnly = false, Destructive = false)]
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
        [Description("Linked notes. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Linked tags. " + ToolDescriptionFragments.OmitToKeepEmptyClears + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("Private flag. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "replace")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            using var updateLease = await client.BeginUpdateAsync();
            if (repoType != null)
            {
                var typeError = await TypeCache.ValidateTypeAsync(repoType, "repository_types", client);
                if (typeError != null) throw McpToolErrors.ValidationError(typeError);
            }

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "repositories");
            var repo = await client.GetOrNullIfNotFoundAsync<GrampsRepository>(
                $"/api/repositories/{Uri.EscapeDataString(resolvedHandle)}");
            if (repo == null)
                return NotFoundHelper.NotFoundMessage("Repository", handle);

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
                NoteList = LinkUpdates.Apply(repo.NoteList, (string[]?)noteHandles, linkMode, x => x),
                TagList = LinkUpdates.Apply(repo.TagList, (string[]?)tagHandles, linkMode, x => x),
                Private = isPrivate ?? repo.Private
            };

            await client.PutMutationAsync($"/api/repositories/{Uri.EscapeDataString(resolvedHandle)}", updateRequest);
            return ResponseEnvelope.UpdateSuccess("Repository", repo.Handle, repo.GrampsId);
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
