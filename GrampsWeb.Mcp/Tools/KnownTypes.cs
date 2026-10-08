using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>Checks the type values that the write tools send.</summary>
internal static class KnownTypes
{
    /// <summary>
    /// Person, family, event and media attributes share the standard attribute types, while Gramps keeps their custom
    /// types per kind of object; a custom type the tree uses on one kind is accepted on the others.
    /// </summary>
    private static readonly string[] AttributeCategories =
        ["attribute_types", "person_attribute_types", "family_attribute_types", "event_attribute_types", "media_attribute_types"];

    /// <summary>
    /// Returns <paramref name="value"/> in the spelling of the <paramref name="category"/> vocabulary, since Gramps
    /// would store a type sent in another case as a new custom type, and rejects a value that is neither a standard
    /// Gramps type nor a custom one the tree already uses, with suggestions.
    /// </summary>
    public static Task<string> ResolveAsync(string value, string category, GrampsApiClient client) =>
        ResolveCategoriesAsync(value, [category], null, client);

    /// <summary>
    /// Checks the name type and the origin of each surname like <see cref="ResolveAsync(string, string, GrampsApiClient)"/>.
    /// Like the checks below, writes each value back in its known spelling and leaves an empty one empty, which
    /// Gramps reads as its default (here Birth Name and no origin).
    /// </summary>
    public static async Task CheckNameAsync(GrampsName? name, string argument, GrampsApiClient client)
    {
        if (name is null)
            return;
        name.Type = await ResolveOptionalAsync(name.Type, ["name_types"], argument, client);
        foreach (var surname in name.SurnameList ?? [])
            surname.OriginType = await ResolveOptionalAsync(surname.OriginType, ["name_origin_types"], argument, client);
    }

    public static async Task CheckNamesAsync(GrampsName[]? names, string argument, GrampsApiClient client)
    {
        foreach (var name in names ?? [])
            await CheckNameAsync(name, argument, client);
    }

    /// <summary>Checks the types of person, family, event and media attributes.</summary>
    public static async Task CheckAttributesAsync(GrampsAttribute[]? attributes, GrampsApiClient client)
    {
        foreach (var attribute in attributes ?? [])
            attribute.Type = await ResolveOptionalAsync(attribute.Type, AttributeCategories, "attributes", client);
    }

    /// <summary>Checks the types of source and citation attributes, which have a vocabulary of their own.</summary>
    public static async Task CheckSourceAttributesAsync(GrampsAttribute[]? attributes, GrampsApiClient client)
    {
        foreach (var attribute in attributes ?? [])
            attribute.Type = await ResolveOptionalAsync(attribute.Type, ["source_attribute_types"], "attributes", client);
    }

    public static async Task CheckUrlsAsync(GrampsUrl[]? urls, GrampsApiClient client)
    {
        foreach (var url in urls ?? [])
            url.Type = await ResolveOptionalAsync(url.Type, ["url_types"], "urls", client);
    }

    /// <summary>
    /// Checks the father's and mother's relation of each child link. linkMode remove matches links by handle only, so
    /// their relations are not checked.
    /// </summary>
    public static async Task CheckChildRefsAsync(GrampsChildRef[]? refs, GrampsApiClient client, string linkMode = "replace")
    {
        if (linkMode == "remove")
            return;
        foreach (var childRef in refs ?? [])
        {
            childRef.FatherRelType = await ResolveOptionalAsync(childRef.FatherRelType, ["child_reference_types"], "childRefs", client);
            childRef.MotherRelType = await ResolveOptionalAsync(childRef.MotherRelType, ["child_reference_types"], "childRefs", client);
        }
    }

    /// <summary>Checks the media type of each repository link; as with child links, linkMode remove is not checked.</summary>
    public static async Task CheckRepositoryRefsAsync(GrampsRepositoryRef[]? refs, GrampsApiClient client, string linkMode = "replace")
    {
        if (linkMode == "remove")
            return;
        foreach (var repositoryRef in refs ?? [])
            repositoryRef.MediaType = await ResolveOptionalAsync(repositoryRef.MediaType, ["source_media_types"], "repositoryHandles", client);
    }

    private static async Task<string?> ResolveOptionalAsync(
        string? value, IReadOnlyList<string> categories, string argument, GrampsApiClient client) =>
        string.IsNullOrWhiteSpace(value) ? value : await ResolveCategoriesAsync(value.Trim(), categories, argument, client);

    private static async Task<string> ResolveCategoriesAsync(
        string value, IReadOnlyList<string> categories, string? argument, GrampsApiClient client)
    {
        var (label, error) = await TypeCache.ResolveTypeAsync(value, categories, client);
        if (error is null)
            return label!;
        throw McpToolErrors.ValidationError(argument is null ? error : $"{argument}: {error}");
    }
}
