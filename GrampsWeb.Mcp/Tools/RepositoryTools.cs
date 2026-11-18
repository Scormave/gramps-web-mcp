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

    /// <summary>The checked request body of a new repository for create_records.</summary>
    internal static async Task<CreateRepositoryRequest> BuildRepositoryRequestAsync(
        string name, string? repoType, FlexibleAddressList? addresses, FlexibleUrlList? urls,
        FlexibleHandleList? noteHandles, FlexibleHandleList? tagHandles, bool isPrivate, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw McpToolErrors.ValidationError("Error: name is required");

        if (repoType != null)
            repoType = await KnownTypes.ResolveAsync(repoType, "repository_types", client);
        await KnownTypes.CheckUrlsAsync(urls, client);
        await new LinkIds()
            .Addresses("addresses", addresses)
            .Handles("noteHandles", noteHandles, "notes")
            .Handles("tagHandles", tagHandles, "tags")
            .ResolveAsync(client);

        return new CreateRepositoryRequest
        {
            Name = name,
            Type = repoType,
            AddressList = (GrampsAddress[]?)addresses is { Length: > 0 } a ? a : null,
            UrlList = (GrampsUrl[]?)urls is { Length: > 0 } u ? u : null,
            NoteList = noteHandles,
            TagList = tagHandles,
            Private = isPrivate
        };
    }

    [McpServerTool(Title = "Update Repository", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing repository's name, type, addresses, website links or links to notes and tags. " +
        "For a new repository use create_records; to remove one, delete_records. Which sources it holds, with their " +
        "call numbers, is stored on the sources: change that with update_source (repositoryHandles), not here. " +
        "addresses sets street, city, postal code, country and the other address fields, or just the street when " +
        "given as one plain line, and urls sets links with their type and description, a bare https://… link being " +
        "a Web Home link. Each replaces every stored address or link, and addresses or urls [] removes them. " +
        "A change shows on every source that lists the repository. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing repository returns a not-found message, and an unknown type is " +
        "rejected before anything is saved. " + ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> UpdateRepository(
        [Description("The repository to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string identifier,
        [Description("New name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("New type, e.g. Archive, Library, Church. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? repoType = null,
        [Description(AddressesText + " " + ToolDescriptionFragments.ReplacedListOnUpdate)]
        FlexibleAddressList? addresses = null,
        [Description(UrlsText + " " + ToolDescriptionFragments.ReplacedListOnUpdate)]
        FlexibleUrlList? urls = null,
        [Description("Notes. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.DescriptionHint)]
        FlexibleHandleList? noteHandles = null,
        [Description("Tags. " + ToolDescriptionFragments.LinkListOnUpdate + " " + FlexibleHandleList.TagHint)]
        FlexibleHandleList? tagHandles = null,
        [Description("true makes the record private, false public. " + ToolDescriptionFragments.OmitToKeepScalar)]
        bool? isPrivate = null,
        GrampsApiClient client = null!,
        [Description(LinkUpdates.Description)]
        string linkMode = "add")
    {
        try
        {
            LinkUpdates.Validate(linkMode);
            await KnownTypes.CheckUrlsAsync(urls, client);
            await new LinkIds()
                .Addresses("addresses", addresses)
                .Handles("noteHandles", noteHandles, "notes")
                .Handles("tagHandles", tagHandles, "tags")
                .ResolveAsync(client, "Nothing was changed.");
            using var updateLease = await client.BeginUpdateAsync();
            if (repoType != null)
                repoType = await KnownTypes.ResolveAsync(repoType, "repository_types", client);

            var resolvedHandle = await HandleResolver.ResolveToHandleAsync(identifier, client, "repositories");
            var repo = await GrampsObjectPatch.LoadAsync(client, $"/api/repositories/{Uri.EscapeDataString(resolvedHandle)}");
            if (repo == null)
                return NotFoundHelper.NotFoundMessage("Repository", identifier);

            repo.Set("name", name);
            repo.Set("type", repoType);
            repo.Set("address_list", (GrampsAddress[]?)addresses);
            repo.Set("urls", (GrampsUrl[]?)urls);
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

    private const string AddressesText =
        "Postal addresses with separate fields, e.g. \"street: 12 High Street\\ncity: London\\npostal: SW1A 1AA\\n" +
        "country: United Kingdom\". " + FlexibleAddressList.DescriptionHint;

    private const string UrlsText =
        "Links with a type and an optional description, e.g. \"Web Home: https://archive.example.org — reading room\\n" +
        "E-mail: info@archive.example.org\". " + FlexibleUrlList.DescriptionHint;
}
