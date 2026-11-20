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

    [McpServerTool(Title = "Get Object", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Read one record in full: its fields, its links to other records, and the records that point to it (backlinks, " +
        "read-only here). Use it once you know the Gramps ID or handle; to find a record by text use search, to browse " +
        "a type use list_objects, and for a person's relatives or dated events get_person_tree and get_timeline are shorter. " +
        "A Gramps ID in the default format, such as I0001, names its type by prefix, so objectType is needed only with " +
        "a handle or an ID in another format; with objectType the identifier is looked up among the records of that " +
        "type, whatever its prefix. extended (people and families only) puts each " +
        "event's date and place on its line, names the others in an event where the person is not Primary, such as a " +
        "child's birth, and adds note text, tag names, citations and media in the same call. Returns plain text for one " +
        "record, without paging: a header with its type, label, handle and Gramps ID, then its fields, its links one per " +
        "line with label, handle and, for events, the role, and \"Referenced by\" sections for the backlinks. A missing record returns a " +
        "not-found message with a hint.")]
    public static async Task<string> GetObject(
        [Description("Gramps ID (e.g. I0001) or handle. The prefixes of the default ID format give the type: I person, F family, E event, P place, S source, C citation, N note, O media, R repository; tags have no Gramps ID.")]
        string identifier,
        [Description("Record type: person, family, event, place, source, citation, note, media, repository or tag. Omit for a Gramps ID in the default format; required for a handle or an ID in another format, such as S_ABC.")]
        string? objectType = null,
        [Description("People and families only: also resolve the linked records (event dates and places, note text, tag names, citations, media) in the same call; default false is faster. Rejected for other types.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var normalizedType = ResolveObjectType(identifier, objectType);
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

    /// <summary>
    /// The record type of <paramref name="identifier"/>, such as <c>person</c>: <paramref name="objectType"/> when
    /// given, else the type the prefix of a Gramps ID in the default format names. Rejects an unknown type and an
    /// identifier without a type that is not such an ID. A given type wins over the prefix, since trees may use other
    /// prefixes; the identifier is then looked up among the records of that type.
    /// </summary>
    internal static string ResolveObjectType(string identifier, string? objectType)
    {
        var inferredType = InferObjectType(identifier);
        var normalizedType = objectType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedType))
        {
            if (inferredType is null)
            {
                throw McpToolErrors.ValidationError(
                    "objectType is required when identifier is a handle or a Gramps ID in a custom format (the default format is " +
                    "one type letter and digits, such as I0001). Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
            }

            normalizedType = inferredType;
        }

        if (!ObjectKinds.ContainsKey(normalizedType))
        {
            throw McpToolErrors.ValidationError(
                "Invalid objectType. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
        }

        return normalizedType;
    }

    /// <summary>The Gramps class name (<c>Person</c>) and API collection (<c>people</c>) of a resolved type.</summary>
    internal static (string ClassName, string ApiPath) KindOf(string objectType) =>
        ObjectKinds[objectType] is var kind ? (kind.DisplayName, kind.ApiPath) : default;

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

    [McpServerTool(Title = "Delete Object", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [McpMeta("anthropic/requiresUserInteraction", true)]
    [Description(
        "Delete one record; undo_change with the deletion's transaction from get_recent_changes restores it. Call it only after the user confirmed " +
        "this exact record. If other records still point to it, nothing is deleted and the reply lists those backlinks " +
        "by type; unlink them first with the owners' update tools, or pass force=true after a separate confirmation, " +
        "and Gramps Web removes those links. Gramps Web also deletes the citations of a deleted source and a family " +
        "left without father, mother and children; the places inside a deleted place keep a link to it, so change " +
        "their enclosedBy with update_place first. " +
        "Clients that support it also show the user a confirmation dialog before deleting; action: declined or cancelled " +
        "means the user said no and nothing was deleted. On success returns the type, action: deleted and the handle; " +
        "a missing record returns a not-found message.")]
    public static async Task<string> DeleteObject(
        [Description("Record type: person, family, event, place, source, citation, note, media, repository or tag.")]
        string objectType,
        [Description("The record to delete. " + ToolDescriptionFragments.HandleDiscovery)]
        string handle,
        [Description("true deletes even when other records still point to it, and Gramps Web removes those links " +
                     "(places inside a place keep theirs); only after the user confirmed that (default false).")]
        bool force = false,
        GrampsApiClient client = null!,
        McpServer server = null!,
        CancellationToken cancellationToken = default) =>
        await DeleteObjectAsync(objectType, handle, force, client,
            UserConfirmation.For(server, "delete_object"), cancellationToken);

    internal static async Task<string> DeleteObjectAsync(string objectType, string handle, bool force,
        GrampsApiClient client, IUserConfirmation confirmation, CancellationToken cancellationToken = default)
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
                client, kind.DisplayName, kind.ApiPath, resolvedHandle, force, handle, confirmation, cancellationToken);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
