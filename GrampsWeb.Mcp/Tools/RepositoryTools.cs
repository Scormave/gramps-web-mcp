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
        "Create a repository: an archive, library, church, website or other holder of sources. " +
        "Returns the new handle, Gramps ID and name, with next steps. Does not check for duplicates: " +
        "look through list_objects(objectType: \"repositories\") first so one holder is not entered twice; " +
        "change an existing repository with update_repository. Link sources to it afterwards through repositoryHandles " +
        "on create_source or update_source, which also carry the call number. noteHandles and tagHandles only link " +
        "existing notes and tags (create_note, create_tag); nothing else is created or changed. " +
        "An unknown type is rejected before anything is saved.")]
    public static async Task<string> CreateRepository(
        [Description("Name, e.g. \"State Archive in Warsaw\" (required).")]
        string name,
        [Description("Repository type, e.g. Archive, Library, Church, Web site (optional; Gramps stores Library when omitted). " +
                     ToolDescriptionFragments.KnownType)]
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
                repoType = await KnownTypes.ResolveAsync(repoType, "repository_types", client);

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
        "Change an existing repository: name, type, address, website, notes, tags or the private flag. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing repository returns a not-found message, and an unknown type is " +
        "rejected before anything is saved. Which sources it holds is stored on the sources (their repositoryHandles), not here.")]
    public static async Task<string> UpdateRepository(
        [Description("The repository to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("New type, e.g. Archive, Library, Church. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? repoType = null,
        [Description("New postal address as one line; replaces every stored address, and an empty string removes them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? address = null,
        [Description("New website; replaces every stored link, and an empty string removes them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? url = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
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
                repoType = await KnownTypes.ResolveAsync(repoType, "repository_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(handle, client, "repositories");
            var repo = await GrampsObjectPatch.LoadAsync(client, $"/api/repositories/{Uri.EscapeDataString(resolvedHandle)}");
            if (repo == null)
                return NotFoundHelper.NotFoundMessage("Repository", handle);

            repo.Set("name", name);
            repo.Set("type", repoType);
            if (address != null)
                repo.Root["address_list"] = GrampsObjectPatch.ToNode(RepositoryAddressListFromStreet(address));
            if (url != null)
                repo.Root["urls"] = GrampsObjectPatch.ToNode(RepositoryUrlListFromPath(url));
            repo.ApplyHandles("note_list", noteHandles, linkMode);
            repo.ApplyHandles("tag_list", tagHandles, linkMode);
            repo.Set("private", isPrivate);

            await repo.SaveAsync(client);
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
