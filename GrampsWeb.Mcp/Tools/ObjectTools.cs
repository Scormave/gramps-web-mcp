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

    /// <summary>The most records one get_records call reads.</summary>
    internal const int MaxReadRecords = 20;

    private const string ObjectTypeList = "person, family, event, place, source, citation, note, media, repository, tag";

    [McpServerTool(Title = "Get Records", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Read up to 20 records of any types in full: each one's fields, its links to other records, and the records " +
        "that point to it (backlinks, read-only here). Use it once you know the Gramps IDs or handles, and pass all the " +
        "records you need in one call; to find records by text use search, to browse a type use list_records, and for " +
        "a person's relatives or dated events get_person_tree and get_timeline are shorter. A Gramps ID in the default " +
        "format, such as I0001, names its type by prefix, so objectType is needed only for handles and IDs in another " +
        "format, such as S_ABC; with objectType every identifier is looked up among the records of that type, whatever " +
        "its prefix. extended (people and families) puts each event's date and place on its line, " +
        "names the others in an event where the person is not Primary, such as a child's birth, and adds note text, " +
        "tag names, citations and media in the same call. Returns plain text without paging, the records in the order " +
        "given and separated by a line \"---\": for each a header with its type, label, handle and Gramps ID, then its " +
        "fields, its links one per line with label, handle and, for events, the role, and \"Referenced by\" sections " +
        "for the backlinks. A record the tree lacks gets a not-found message with a hint in its place, and the others " +
        "are still read.")]
    public static async Task<string> GetRecords(
        [Description("Up to 20 Gramps IDs or handles, one record as a one-element array. The prefixes of the default " +
                     "ID format give the type: I person, F family, E event, P place, S source, C citation, N note, " +
                     "O media, R repository; tags have no Gramps ID.")]
        string[] identifiers,
        [Description("Record type of all the identifiers: person, family, event, place, source, citation, note, " +
                     "media, repository or tag. Omit when every identifier is a Gramps ID in the default format; " +
                     "required for handles and IDs in another format, such as S_ABC, and then every identifier is " +
                     "looked up among the records of this type.")]
        string? objectType = null,
        [Description("People and families: also resolve the linked records (event dates and places, note text, tag " +
                     "names, citations, media) in the same call; default false is faster. Other records are read as " +
                     "usual; rejected when none of the records is a person or family.")]
        bool extended = false,
        GrampsApiClient client = null!)
    {
        try
        {
            var records = TypeIdentifiers(identifiers, objectType, MaxReadRecords, "reads");
            if (extended && !records.Any(r => r.Type is "person" or "family"))
            {
                throw McpToolErrors.ValidationError(
                    "extended is supported only for people and families, and identifiers names neither.");
            }

            using var gate = new SemaphoreSlim(4);
            var texts = await Task.WhenAll(records.Select(async record =>
            {
                await gate.WaitAsync();
                try
                {
                    return await ReadRecordAsync(record.Identifier, record.Type, extended, client);
                }
                finally
                {
                    gate.Release();
                }
            }));
            return string.Join("\n\n---\n\n", texts.Select(text => text.TrimEnd()));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// The records named by <paramref name="identifiers"/>, in the order given and each identifier once, with the type
    /// <paramref name="objectType"/> when given, else the type the prefix of a Gramps ID in the default format names;
    /// at most <paramref name="max"/> of them, which one call <paramref name="verb"/>. A given type wins over the
    /// prefix, since trees may use other prefixes: every identifier is then looked up among the records of that type.
    /// </summary>
    private static IReadOnlyList<(string Identifier, string Type)> TypeIdentifiers(
        IReadOnlyList<string>? identifiers, string? objectType, int max, string verb)
    {
        if (identifiers is null || identifiers.Count == 0)
            throw McpToolErrors.ValidationError("identifiers must name at least one record.");
        if (identifiers.Any(string.IsNullOrWhiteSpace))
            throw McpToolErrors.ValidationError("identifiers must not contain empty entries.");

        var distinct = identifiers.Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > max)
            throw McpToolErrors.ValidationError($"identifiers names {distinct.Count} records; one call {verb} at most {max}.");

        var given = objectType?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(given))
            given = null;
        else if (!ObjectKinds.ContainsKey(given))
            throw McpToolErrors.ValidationError($"Invalid objectType. Must be one of: {ObjectTypeList}.");

        return distinct.Select(identifier =>
        {
            var type = given ?? InferObjectType(identifier);
            if (type is null)
            {
                throw McpToolErrors.ValidationError(
                    $"objectType is required for '{identifier}', a handle or a Gramps ID in a custom format (the default " +
                    $"format is one type letter and digits, such as I0001). Must be one of: {ObjectTypeList}.");
            }
            return (identifier, type);
        }).ToList();
    }

    private static async Task<string> ReadRecordAsync(string identifier, string type, bool extended,
        GrampsApiClient client) => type switch
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

    /// <summary>
    /// The record type of one <paramref name="identifier"/>, such as <c>person</c>, checked as
    /// <see cref="TypeIdentifiers"/> checks each identifier of a call.
    /// </summary>
    internal static string ResolveObjectType(string identifier, string? objectType) =>
        TypeIdentifiers([identifier], objectType, 1, "reads").Single().Type;

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
        "Gramps ID), the records Gramps Web deleted along or removed links from, and the events, citations, notes " +
        "and media that only the deleted records linked to: Gramps Web keeps them, so ask the user whether to delete " +
        "them too. If a later type fails, action: partially_deleted lists what was and was not deleted.")]
    public static async Task<string> DeleteRecords(
        [Description("Up to 50 Gramps IDs (e.g. I0001, F0002; the prefix of the default format gives the type) or " +
                     "handles, of any types; find them with search or list_records.")]
        string[] identifiers,
        [Description("Record type of all the identifiers: person, family, event, place, source, citation, note, " +
                     "media, repository or tag. Omit when every identifier is a Gramps ID in the default format; " +
                     "required for handles and IDs in another format, such as S_ABC, and then every identifier is " +
                     "looked up among the records of this type.")]
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
        var typed = TypeIdentifiers(identifiers, objectType, DeleteHelper.MaxRecords, "deletes")
            .Select(t => (t.Identifier, Collection: ObjectKinds[t.Type].ApiPath))
            .ToList();
        // A read-only server refuses before looking anything up.
        client.EnsureWritable();

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
