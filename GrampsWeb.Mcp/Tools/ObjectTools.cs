using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading one record and deleting records of any supported type.
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
        "A Gramps ID names its type by prefix, so objectType is needed only with a handle, and an objectType that " +
        "disagrees with the prefix is rejected rather than guessed. extended (people and families only) puts each " +
        "event's date and place on its line, names the others in an event where the person is not Primary, such as a " +
        "child's birth, and adds note text, tag names, citations and media in the same call. Returns plain text for one " +
        "record, without paging: a header with its type, label, handle and Gramps ID, then its fields, its links one per " +
        "line with label, handle and, for events, the role, and \"Referenced by\" sections for the backlinks. A missing record returns a " +
        "not-found message with a hint.")]
    public static async Task<string> GetObject(
        [Description("Gramps ID (e.g. I0001) or handle. ID prefixes give the type: I person, F family, E event, P place, S source, C citation, N note, O media, R repository; tags have no Gramps ID.")]
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
                _ => null
            }
            : null;

    [McpServerTool(Title = "Delete Records", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [McpMeta("anthropic/requiresUserInteraction", true)]
    [Description(
        "Delete up to 50 records of any types, only after the user confirmed these exact records. Records are checked " +
        "together: if any is missing, nothing is deleted. If records outside the call link to them, nothing is deleted " +
        "and the reply lists those links by type; unlink them first with the linking records' update tools " +
        "(linkMode \"remove\"), add those records to the call, or pass force=true after a separate confirmation, and " +
        "Gramps Web removes the links. Gramps Web also deletes the citations of a deleted source and a family left " +
        "without father, mother and children. A place that encloses places outside the call is never deleted, even " +
        "with force: change their enclosedBy with update_place first. Clients that support it show the user a " +
        "confirmation dialog first; action: declined or cancelled means the user said no and nothing was deleted. " +
        "Each record type is deleted in its own transaction, families first, then people, events, places, media, " +
        "citations, sources, repositories, notes and tags; undo_change on each transaction from get_recent_changes, " +
        "newest first, restores the records. Returns action: deleted with one line per record (type, label, handle, " +
        "Gramps ID) and the records Gramps Web deleted along or removed links from; if a later type fails, action: " +
        "partially_deleted lists what was and was not deleted.")]
    public static async Task<string> DeleteRecords(
        [Description("Up to 50 Gramps IDs (e.g. I0001, F0002; the prefix gives the type) or handles, of any types; " +
                     "find them with search or list_objects.")]
        string[] identifiers,
        [Description("Record type of the handles: person, family, event, place, source, citation, note, media, " +
                     "repository or tag. Omit when every identifier is a Gramps ID; required for handles, which are " +
                     "then all of this type, and a Gramps ID of another type is rejected.")]
        string? objectType = null,
        [Description("true deletes even when records outside the call link to them, and Gramps Web removes those " +
                     "links; only after the user confirmed that (default false).")]
        bool force = false,
        GrampsApiClient client = null!,
        McpServer server = null!,
        CancellationToken cancellationToken = default) =>
        await DeleteRecordsAsync(identifiers, objectType, force, client,
            UserConfirmation.For(server, "delete_records"), cancellationToken);

    internal static async Task<string> DeleteRecordsAsync(IReadOnlyList<string>? identifiers, string? objectType,
        bool force, GrampsApiClient client, IUserConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        try
        {
            var targets = await ResolveTargetsAsync(identifiers, objectType, client);
            return await DeleteHelper.DeleteAsync(client, targets, force, confirmation, cancellationToken);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// The records to delete, in the order given and each once: Gramps IDs resolved to handles, with the type
    /// from the prefix or <paramref name="objectType"/>.
    /// </summary>
    private static async Task<IReadOnlyList<DeleteHelper.Target>> ResolveTargetsAsync(
        IReadOnlyList<string>? identifiers, string? objectType, GrampsApiClient client)
    {
        if (identifiers is null || identifiers.Count == 0)
            throw McpToolErrors.ValidationError("identifiers must name at least one record.");
        if (identifiers.Count > DeleteHelper.MaxRecords)
        {
            throw McpToolErrors.ValidationError(
                $"identifiers names {identifiers.Count} records; one call deletes at most {DeleteHelper.MaxRecords}.");
        }
        if (identifiers.Any(string.IsNullOrWhiteSpace))
            throw McpToolErrors.ValidationError("identifiers must not contain empty entries.");

        ObjectKind? given = null;
        var normalizedType = objectType?.Trim();
        if (!string.IsNullOrEmpty(normalizedType))
        {
            if (!ObjectKinds.TryGetValue(normalizedType, out given))
            {
                throw McpToolErrors.ValidationError(
                    "Invalid objectType. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
            }
            normalizedType = normalizedType.ToLowerInvariant();
        }

        var typed = identifiers.Select(raw =>
        {
            var identifier = raw.Trim();
            var inferred = InferObjectType(identifier);
            if (inferred is null && given is null)
            {
                throw McpToolErrors.ValidationError(
                    $"objectType is required for the handle '{identifier}'. Must be one of: person, family, event, place, source, citation, note, media, repository, tag.");
            }
            if (inferred is not null && given is not null && inferred != normalizedType)
            {
                throw McpToolErrors.ValidationError(
                    $"objectType '{normalizedType}' does not match Gramps ID '{identifier}', which identifies a {inferred}.");
            }
            return (Identifier: identifier, Collection: inferred is null ? given!.ApiPath : ObjectKinds[inferred].ApiPath);
        }).ToList();

        using var gate = new SemaphoreSlim(4);
        var resolved = await Task.WhenAll(typed.Select(async t =>
        {
            await gate.WaitAsync();
            try
            {
                var handle = await HandleResolver.ResolveToHandleAsync(t.Identifier, client, t.Collection);
                return new DeleteHelper.Target(t.Collection, handle.Trim(), t.Identifier);
            }
            finally
            {
                gate.Release();
            }
        }));
        return resolved.DistinctBy(t => (t.Collection, t.Handle)).ToList();
    }
}
