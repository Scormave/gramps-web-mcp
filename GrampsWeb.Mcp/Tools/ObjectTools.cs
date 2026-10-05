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
        "Read one record: its fields, its links to other records, and the records that point to it (backlinks, read-only here). " +
        "Use it once you know the Gramps ID or handle; " +
        "to find a record by text use search, to browse a type use list_objects. A missing record returns a not-found " +
        "message with a hint. For a person's relatives or dated events, get_person_tree and get_timeline are shorter.")]
    public static async Task<string> GetObject(
        [Description("Gramps ID (e.g. I0001) or handle. ID prefixes give the type: I person, F family, E event, P place, S source, C citation, N note, O media, R repository, T tag.")]
        string identifier,
        [Description("Record type: person, family, event, place, source, citation, note, media, repository or tag. Omit for a Gramps ID; required for a handle.")]
        string? objectType = null,
        [Description("People and families only: also resolve the linked records (event dates and places, note text, tag names, citations, media) in the same call; default false is faster. Rejected for other types.")]
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
        "Permanently delete one record; it cannot be undone through this server. Call it only after the user confirmed " +
        "this exact record. If other records still point to it, nothing is deleted and the reply lists those backlinks " +
        "by type; unlink them first with the owners' update tools, or pass force=true after a separate confirmation. " +
        "On success returns the type, action: deleted and the handle; a missing record returns a not-found message.")]
    public static async Task<string> DeleteObject(
        [Description("Record type: person, family, event, place, source, citation, note, media, repository or tag.")]
        string objectType,
        [Description("The record to delete. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("true deletes even when other records still point to it, leaving those links dangling (default false).")]
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
