using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool for reading a single Gramps object of any supported type.
/// </summary>
[McpServerToolType]
public static class ObjectTools
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

    [McpServerTool(Title = "Get Object", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: fetch one object by handle or Gramps ID. For a Gramps ID, objectType is inferred from its prefix. " +
        "For an opaque handle, objectType must be person, family, event, place, source, citation, note, media, repository, or tag. " +
        "extended=true resolves linked details for people and families only; it is not supported for other object types.")]
    public static async Task<string> GetObject(
        string identifier,
        [Description("Optional for a Gramps ID, whose prefix determines the type. Required for an opaque handle: person | family | event | place | source | citation | note | media | repository | tag.")]
        string? objectType = null,
        [Description("For person and family only: resolve linked objects inline. Default: false.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var inferredType = InferObjectType(identifier);
            var normalizedType = objectType?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedType))
            {
                if (inferredType is null)
                {
                    throw McpToolErrors.ValidationError(
                        "objectType is required when identifier is an opaque handle. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
                }

                normalizedType = inferredType;
            }

            if (normalizedType is not ("person" or "family" or "event" or "place" or "source" or
                "citation" or "note" or "media" or "repository" or "tag"))
            {
                throw McpToolErrors.ValidationError(
                    "Invalid objectType. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
            }

            if (inferredType is not null && !string.Equals(normalizedType, inferredType, StringComparison.Ordinal))
            {
                throw McpToolErrors.ValidationError(
                    $"objectType '{normalizedType}' does not match Gramps ID '{identifier}', which identifies a {inferredType}.");
            }

            if (extended && normalizedType is not ("person" or "family"))
            {
                throw McpToolErrors.ValidationError(
                    "extended is supported only for objectType person or family.");
            }

            return normalizedType switch
            {
                "person" => await PersonTools.ReadPersonAsync(identifier, extended, client),
                "family" => await FamilyTools.ReadFamilyAsync(identifier, extended, client),
                "event" => await EventTools.ReadEventAsync(identifier, client),
                "place" => await PlaceTools.ReadPlaceAsync(identifier, client),
                "source" => await SourceTools.ReadSourceAsync(identifier, client),
                "citation" => await CitationTools.ReadCitationAsync(identifier, client),
                "note" => await NoteTools.ReadNoteAsync(identifier, client),
                "media" => await MediaTools.ReadMediaAsync(identifier, client),
                "repository" => await RepositoryTools.ReadRepositoryAsync(identifier, client),
                "tag" => await TagTools.ReadTagAsync(identifier, client),
                _ => throw new InvalidOperationException("Validated object type was not dispatched.")
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static string? InferObjectType(string identifier) =>
        HandleResolver.LooksLikeGrampsId(identifier)
            ? HandleResolver.PrefixToObjectType(identifier[0]) switch
            {
                "people" => "person",
                "families" => "family",
                "events" => "event",
                "places" => "place",
                "sources" => "source",
                "citations" => "citation",
                "notes" => "note",
                "media" => "media",
                "repositories" => "repository",
                "tags" => "tag",
                _ => null
            }
            : null;

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
            {
                throw McpToolErrors.ValidationError(
                    "Invalid objectType. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
            }

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
