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

    [McpServerTool(Title = "Create Repository", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Create a repository: an archive, library, church, website or other holder of sources. " +
        "Returns the new handle, Gramps ID and name, with next steps. Does not check for duplicates: " +
        "look through list_objects(objectType: \"repositories\") first so one holder is not entered twice; " +
        "change an existing repository with update_repository. Link sources to it afterwards through repositoryHandles " +
        "on create_source or update_source, which also carry the call number. noteHandles and tagHandles only link " +
        "existing notes and tags (create_note, create_tag); nothing else is created or changed. " +
        "An unknown type is rejected before anything is saved. " + ToolDescriptionFragments.BatchCreate + " " +
        ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> CreateRepository(
        [Description("Name, e.g. \"State Archive in Warsaw\" (required).")]
        string name,
        [Description("Repository type, e.g. Archive, Library, Church, Web site (optional; Gramps stores Library when omitted). " +
                     ToolDescriptionFragments.KnownType)]
        string? repoType = null,
        [Description("Postal address as one line, e.g. \"12 High Street, London\", stored as the street of the repository's address " +
                     "(optional). To keep the city, postal code or country in their own fields, use addresses instead.")]
        string? address = null,
        [Description(AddressesText + " Optional; not together with address.")]
        FlexibleAddressList? addresses = null,
        [Description("Website, e.g. https://archive.example.org, stored as the repository's Web Home link (optional). " +
                     "For several links, another type such as E-mail, or a description, use urls instead.")]
        string? url = null,
        [Description(UrlsText + " Optional; not together with url.")]
        FlexibleUrlList? urls = null,
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
            var request = await BuildRepositoryRequestAsync(name, repoType, address, addresses, url, urls, noteHandles,
                tagHandles, isPrivate, client);
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

    /// <summary>The checked request body of create_repository; create_records sends it too.</summary>
    internal static async Task<CreateRepositoryRequest> BuildRepositoryRequestAsync(
        string name, string? repoType, string? address, FlexibleAddressList? addresses, string? url, FlexibleUrlList? urls,
        FlexibleHandleList? noteHandles, FlexibleHandleList? tagHandles, bool isPrivate, GrampsApiClient client)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw McpToolErrors.ValidationError("Error: name is required");
        CheckOneAddressForm(address, addresses);
        CheckOneUrlForm(url, urls);

        if (repoType != null)
            repoType = await KnownTypes.ResolveAsync(repoType, "repository_types", client);
        await KnownTypes.CheckUrlsAsync(urls, client);

        return new CreateRepositoryRequest
        {
            Name = name,
            Type = repoType,
            AddressList = (GrampsAddress[]?)addresses is { Length: > 0 } a ? a : RepositoryAddressListFromStreet(address),
            UrlList = (GrampsUrl[]?)urls is { Length: > 0 } u ? u : RepositoryUrlListFromPath(url),
            NoteList = noteHandles,
            TagList = tagHandles,
            Private = isPrivate
        };
    }

    [McpServerTool(Title = "Update Repository", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Change an existing repository's name, type, address, website or links to notes and tags. " +
        "For a new repository use create_repository; to remove one, delete_object. Which sources it holds, with their " +
        "call numbers, is stored on the sources: change that with update_source (repositoryHandles), not here. " +
        "addresses sets street, city, postal code, country and the other address fields, and urls sets links with " +
        "their type and description; address is a shortcut for a single address with only a street line, and url for " +
        "a single Web Home link. Each replaces every stored address or link; an empty address or url string, or " +
        "addresses or urls [], removes them. A change shows on every source that lists the repository. " +
        ToolDescriptionFragments.UpdateSemantics + " " +
        "Returns the handle and Gramps ID; a missing repository returns a not-found message, and an unknown type is " +
        "rejected before anything is saved. " + ToolDescriptionFragments.WriteAccess)]
    public static async Task<string> UpdateRepository(
        [Description("The repository to change. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("New name. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? name = null,
        [Description("New type, e.g. Archive, Library, Church. " + ToolDescriptionFragments.OmitToKeepScalar + " " + ToolDescriptionFragments.KnownType)]
        string? repoType = null,
        [Description("New postal address as one line, stored as the street of a single address; replaces every stored " +
                     "address, and an empty string removes them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? address = null,
        [Description(AddressesText + " Not together with address. " + ToolDescriptionFragments.ReplacedListOnUpdate)]
        FlexibleAddressList? addresses = null,
        [Description("New website, stored as a single Web Home link; replaces every stored link, and an empty string " +
                     "removes them. " + ToolDescriptionFragments.OmitToKeepScalar)]
        string? url = null,
        [Description(UrlsText + " Not together with url. " + ToolDescriptionFragments.ReplacedListOnUpdate)]
        FlexibleUrlList? urls = null,
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
            CheckOneAddressForm(address, addresses);
            CheckOneUrlForm(url, urls);
            await KnownTypes.CheckUrlsAsync(urls, client);
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
            repo.Set("address_list", (GrampsAddress[]?)addresses);
            if (url != null)
                repo.Root["urls"] = GrampsObjectPatch.ToNode(RepositoryUrlListFromPath(url));
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

    /// <summary>address is a one-line shortcut for addresses, so a call gives one or the other.</summary>
    private static void CheckOneAddressForm(string? address, FlexibleAddressList? addresses)
    {
        if (address != null && addresses != null)
            throw McpToolErrors.ValidationError(
                "Error: give address or addresses, not both. address is one street line; addresses takes street, city, " +
                "postal code, country and the other fields.");
    }

    /// <summary>url is a shortcut for one Web Home link in urls, so a call gives one or the other.</summary>
    private static void CheckOneUrlForm(string? url, FlexibleUrlList? urls)
    {
        if (url != null && urls != null)
            throw McpToolErrors.ValidationError(
                "Error: give url or urls, not both. url is one Web Home link; urls takes several links, each with its " +
                "type and description.");
    }

    /// <summary>Single street line as Gramps address_list entry; null input omits the field on create.</summary>
    private static GrampsAddress[]? RepositoryAddressListFromStreet(string? street)
    {
        if (street == null)
            return null;
        if (string.IsNullOrWhiteSpace(street))
            return [];
        return [new GrampsAddress { Street = street.Trim() }];
    }

    /// <summary>Single URL as urls entry; null input omits on create.</summary>
    private static GrampsUrl[]? RepositoryUrlListFromPath(string? path)
    {
        if (path == null)
            return null;
        if (string.IsNullOrWhiteSpace(path))
            return [];
        return [new GrampsUrl { Path = path.Trim(), Type = "Web Home" }];
    }
}
