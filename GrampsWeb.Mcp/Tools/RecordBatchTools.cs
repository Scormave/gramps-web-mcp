using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool that creates many linked records in one Gramps Web transaction (POST <c>/api/objects/</c>).
/// </summary>
[McpServerToolType]
public static class RecordBatchTools
{
    /// <summary>Most records one call may create.</summary>
    internal const int MaxRecords = 50;

    private const int MaxKeyLength = 64;

    /// <summary>Fields holding one handle, by Gramps JSON name: the class they link to and the argument name.</summary>
    private static readonly Dictionary<string, (string Class, string Argument)> HandleFields = new()
    {
        ["place"] = ("Place", "placeHandle"),
        ["source_handle"] = ("Source", "sourceHandle"),
        ["father_handle"] = ("Person", "fatherHandle"),
        ["mother_handle"] = ("Person", "motherHandle"),
    };

    /// <summary>Lists of <c>{ref, …}</c> objects, by Gramps JSON name.</summary>
    private static readonly Dictionary<string, (string Class, string Argument)> RefLists = new()
    {
        ["event_ref_list"] = ("Event", "eventRefs"),
        ["child_ref_list"] = ("Person", "childRefs"),
        ["person_ref_list"] = ("Person", "personAssociations"),
        ["reporef_list"] = ("Repository", "repositoryHandles"),
        ["placeref_list"] = ("Place", "enclosedBy"),
        ["media_list"] = ("Media", "mediaHandles"),
    };

    /// <summary>Lists of handle strings, by Gramps JSON name.</summary>
    private static readonly Dictionary<string, (string Class, string Argument)> HandleLists = new()
    {
        ["citation_list"] = ("Citation", "citationHandles"),
        ["note_list"] = ("Note", "noteHandles"),
        ["tag_list"] = ("Tag", "tagHandles"),
    };

    [McpServerTool(Title = "Create Records", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description(
        "Create many linked records in one call: repositories, places, sources, notes, citations, events, people and " +
        "families. Gramps Web saves them in one transaction, so either all are created or none, and undo_change on " +
        "that transaction (get_recent_changes) removes them all again. Give a record that others link to a key and " +
        "write \"#key\" wherever a handle is taken: a citation's sourceHandle \"#src\", an event's citationHandles " +
        "[\"#cit\"], a person's eventRefs [\"#birth\"], a family's fatherHandle \"#ivan\" and childRefs [\"#petr\"]. " +
        "Existing records are linked by handle, or by Gramps ID where the create tool accepts one. Each record takes " +
        "the arguments of its create tool (create_person, create_event, …), except that people join families only " +
        "through families: a new family's father, mother and children get the link back. Links from existing records " +
        "to new ones, such as a new child of an existing family, are made afterwards with update_* and linkMode " +
        "\"add\". Media and tags are not created here: create them first with create_media and create_tag. Does not " +
        "check for duplicates: search first. Up to 50 records per call; every record is checked before anything is " +
        "saved, and an error names the record, e.g. people[1]. Returns each record's handle and Gramps ID. " +
        ToolDescriptionFragments.WriteAccess + " " + ToolDescriptionFragments.InputGuide)]
    public static async Task<string> CreateRecords(
        [Description("New repositories; fields as in create_repository.")]
        RepositoryRecord[]? repositories = null,
        [Description("New places; fields as in create_place. enclosedBy may name a place of this call by \"#key\".")]
        PlaceRecord[]? places = null,
        [Description("New sources; fields as in create_source.")]
        SourceRecord[]? sources = null,
        [Description("New notes; fields as in create_note. Other records link them through noteHandles.")]
        NoteRecord[]? notes = null,
        [Description("New citations; fields as in create_citation. Other records link them through citationHandles.")]
        CitationRecord[]? citations = null,
        [Description("New events; fields as in create_event. People and families link them through eventRefs.")]
        EventRecord[]? events = null,
        [Description("New people; fields as in create_person, without familyHandles and parentFamilyHandles: " +
                     "name a person in a family's fatherHandle, motherHandle or childRefs instead.")]
        PersonRecord[]? people = null,
        [Description("New families; fields as in create_family. They are saved after the people, which get the link back.")]
        FamilyRecord[]? families = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var entries = Collect(("repositories", repositories), ("places", places), ("sources", sources),
                ("notes", notes), ("citations", citations), ("events", events), ("people", people),
                ("families", families));
            var keys = CheckKeys(entries);

            foreach (var entry in entries)
            {
                try
                {
                    (entry.Request, entry.Label) = await entry.Record.BuildAsync(client);
                }
                catch (McpException ex)
                {
                    throw RecordError(entry, ex);
                }
            }

            var handles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                entry.Handle = NewHandle(handles);
                if (entry.Key != null)
                    keys[entry.Key].Handle = entry.Handle;
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                entry.Json = JsonSerializer.SerializeToNode(entry.Request, entry.Request.GetType(), GrampsJson.Options)!
                    .AsObject();
                entry.Json["handle"] = entry.Handle;
                SubstituteKeys(entry.Json, null, entry, keys, used);
            }

            var newEventTypes = entries.Where(e => e.Key != null && e.Request is CreateEventRequest)
                .ToDictionary(e => "#" + e.Key, e => ((CreateEventRequest)e.Request).Type, StringComparer.Ordinal);
            foreach (var entry in entries.Where(e => e.Request is CreatePersonRequest))
            {
                var person = (CreatePersonRequest)entry.Request;
                await PersonTools.SetVitalEventIndexesAsync(client, person, newEventTypes);
                entry.Json["birth_ref_index"] = person.BirthRefIndex;
                entry.Json["death_ref_index"] = person.DeathRefIndex;
            }

            string body;
            try
            {
                body = await client.PostObjectsAsync(new JsonArray(entries.Select(e => (JsonNode)e.Json).ToArray()));
            }
            catch (GrampsApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                // One transaction: a refused call leaves the tree as it was.
                throw new McpException($"Gramps Web refused the call and saved none of the records. {ex.Message}", ex);
            }
            return FormatResult(entries, used, ParseChanges(body));
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>One record of the call, where it was given, and what is sent for it.</summary>
    private sealed class Entry(BatchRecord record, string path, string? key)
    {
        public BatchRecord Record { get; } = record;

        /// <summary>Where the record was given, e.g. <c>people[1]</c>.</summary>
        public string Path { get; } = path;

        /// <summary>The key without its leading <c>#</c>, or null.</summary>
        public string? Key { get; } = key;

        public object Request { get; set; } = null!;
        public string Label { get; set; } = "";
        public string Handle { get; set; } = "";
        public JsonObject Json { get; set; } = null!;

        /// <summary><c>people[1]</c>, or <c>people[1] (key ivan)</c>.</summary>
        public string Where => Key == null ? Path : $"{Path} (key {Key})";
    }

    /// <summary>A key of the call: the record it names and, once generated, that record's handle.</summary>
    private sealed class KeyTarget(Entry entry)
    {
        public Entry Entry { get; } = entry;
        public string Handle { get; set; } = "";
    }

    /// <summary>The records in saving order: every family after the people it links.</summary>
    private static List<Entry> Collect(params (string Name, BatchRecord?[]? Records)[] groups)
    {
        var entries = new List<Entry>();
        foreach (var (name, records) in groups)
        {
            for (var i = 0; records != null && i < records.Length; i++)
            {
                var path = $"{name}[{i}]";
                var record = records[i] ?? throw McpToolErrors.ValidationError(
                    $"Error: {path} is empty; give each record as an object, or leave it out.");
                var key = record.Key?.Trim().TrimStart('#');
                entries.Add(new Entry(record, path, string.IsNullOrEmpty(key) ? null : key));
            }
        }

        if (entries.Count == 0)
            throw McpToolErrors.ValidationError(
                "Error: nothing to create. Give at least one record in repositories, places, sources, notes, " +
                "citations, events, people or families.");
        if (entries.Count > MaxRecords)
            throw McpToolErrors.ValidationError(
                $"Error: create_records takes up to {MaxRecords} records per call, and this call has {entries.Count}. " +
                "Split it: create the first part, then link to it from the next call by the handles it returns.");
        return entries;
    }

    private static Dictionary<string, KeyTarget> CheckKeys(List<Entry> entries)
    {
        var keys = new Dictionary<string, KeyTarget>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Key != null))
        {
            var key = entry.Key!;
            if (key.Length > MaxKeyLength || !key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                throw McpToolErrors.ValidationError(
                    $"Error: {entry.Path}: key \"{key}\" may hold only letters, digits, _ and -, up to {MaxKeyLength}.");
            if (keys.TryGetValue(key, out var first))
                throw McpToolErrors.ValidationError(
                    $"Error: {entry.Path}: key \"{key}\" is already used by {first.Entry.Path}; keys must be unique in the call.");
            keys[key] = new KeyTarget(entry);
        }

        return keys;
    }

    /// <summary>A check of the create tool that failed, prefixed with the record; server errors pass unchanged.</summary>
    private static McpException RecordError(Entry entry, McpException ex)
    {
        var message = ex.Message.StartsWith("Error: ", StringComparison.Ordinal) ? ex.Message["Error: ".Length..] : ex.Message;
        return new McpException($"Error: {entry.Where}: {message}", ex);
    }

    /// <summary>A handle as Gramps creates one: time in 1/10 ms, then a random number, both hex.</summary>
    private static string NewHandle(HashSet<string> taken)
    {
        string handle;
        do
        {
            handle = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10:x8}{Random.Shared.NextInt64():x8}";
        } while (!taken.Add(handle));

        return handle;
    }

    /// <summary>
    /// Replaces every <c>"#key"</c> in the record's Gramps JSON by the handle of the record the key names,
    /// checking the record is of the class the field links to; other strings stay as they are.
    /// </summary>
    private static void SubstituteKeys(JsonNode? node, string? owner, Entry entry,
        Dictionary<string, KeyTarget> keys, HashSet<string> used)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (value is JsonValue && HandleFields.TryGetValue(name, out var field)
                        || name == "ref" && value is JsonValue && owner != null && RefLists.TryGetValue(owner, out field))
                    {
                        if (Resolve(value, field, entry, keys, used) is { } handle)
                            obj[name] = handle;
                    }
                    else if (value is JsonArray list && HandleLists.TryGetValue(name, out field))
                    {
                        for (var i = 0; i < list.Count; i++)
                        {
                            if (Resolve(list[i], field, entry, keys, used) is { } handle)
                                list[i] = handle;
                        }
                    }
                    else
                    {
                        SubstituteKeys(value, name, entry, keys, used);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                    SubstituteKeys(item, owner, entry, keys, used);
                break;
        }
    }

    /// <summary>The handle a <c>"#key"</c> stands for, or null for any other value.</summary>
    private static string? Resolve(JsonNode? value, (string Class, string Argument) field, Entry entry,
        Dictionary<string, KeyTarget> keys, HashSet<string> used)
    {
        if (StringValue(value) is not { } text || !text.StartsWith('#'))
            return null;

        if (field.Class is "Media" or "Tag")
            throw McpToolErrors.ValidationError(
                $"Error: {entry.Where}: {field.Argument} has \"{text}\", but create_records does not create " +
                $"{(field.Class == "Media" ? "media" : "tags")}: create it first with " +
                $"{(field.Class == "Media" ? "create_media" : "create_tag")} and give its handle.");

        var key = text[1..];
        if (!keys.TryGetValue(key, out var target))
        {
            var known = keys.Where(k => k.Value.Entry.Record.GrampsClass == field.Class).Select(k => "#" + k.Key).ToList();
            var hint = known.Count == 0
                ? $"No {field.Class.ToLowerInvariant()} of this call has a key."
                : $"Keys of {field.Class.ToLowerInvariant()} records in this call: {string.Join(", ", known)}.";
            throw McpToolErrors.ValidationError(
                $"Error: {entry.Where}: {field.Argument} has \"{text}\", which is not a key of this call. {hint}");
        }

        var targetClass = target.Entry.Record.GrampsClass;
        if (targetClass != field.Class)
            throw McpToolErrors.ValidationError(
                $"Error: {entry.Where}: {field.Argument} takes {Article(field.Class)}, but \"{text}\" is " +
                $"{target.Entry.Path}, {Article(targetClass)}.");

        used.Add(key);
        return target.Handle;
    }

    /// <summary>"a person", "an event".</summary>
    private static string Article(string grampsClass) =>
        $"{(grampsClass[0] is 'A' or 'E' or 'I' or 'O' or 'U' ? "an" : "a")} {grampsClass.ToLowerInvariant()}";

    /// <summary>A change of the transaction: what was done to a record and the record as stored.</summary>
    private sealed record Change(string GrampsClass, string Type, JsonObject? New);

    /// <summary>The last change per handle; an unexpected body gives none, and the IDs are then left out.</summary>
    private static Dictionary<string, Change> ParseChanges(string body)
    {
        var changes = new Dictionary<string, Change>(StringComparer.Ordinal);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return changes;
        }

        if (root is not JsonArray array)
            return changes;
        foreach (var item in array.OfType<JsonObject>())
        {
            if (item["handle"]?.GetValueKind() != JsonValueKind.String)
                continue;
            changes[item["handle"]!.GetValue<string>()] = new Change(
                StringValue(item["_class"]) ?? "", StringValue(item["type"]) ?? "", item["new"] as JsonObject);
        }

        return changes;
    }

    private static string? StringValue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string FormatResult(List<Entry> entries, HashSet<string> used, Dictionary<string, Change> changes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Transaction");
        sb.AppendLine("action: created");
        sb.AppendLine($"records: {entries.Count}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Created {entries.Count} records in one transaction: get_recent_changes lists it as " +
                      "\"Add multiple objects\", and undo_change on it removes them all.");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            var grampsId = changes.TryGetValue(entry.Handle, out var change) ? StringValue(change.New?["gramps_id"]) : null;
            sb.AppendLine(Line(entry.Record.GrampsClass, entry.Label, entry.Handle, grampsId, entry.Key));
        }

        var handles = entries.Select(e => e.Handle).ToHashSet(StringComparer.Ordinal);
        var updated = changes.Where(c => c.Value.Type == "update" && !handles.Contains(c.Key)).ToList();
        if (updated.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Existing records that now link back to a new family ({updated.Count}):");
            foreach (var (handle, change) in updated)
                sb.AppendLine(Line(change.GrampsClass, ExistingLabel(change), handle, StringValue(change.New?["gramps_id"]), null));
        }

        var unlinked = entries.Where(e => e.Record is EventRecord or CitationRecord or NoteRecord
                                          && (e.Key == null || !used.Contains(e.Key))).ToList();
        if (unlinked.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Not linked from any record of this call ({unlinked.Count}): " +
                          string.Join(", ", unlinked.Select(e => e.Where)) + ".");
        }

        var unplaced = entries.Where(e => e.Record is PersonRecord && (e.Key == null || !InNewFamily(e.Key, entries)))
            .ToList();

        sb.AppendLine();
        sb.AppendLine("Next steps:");
        if (unlinked.Count > 0)
            sb.AppendLine("  • Link the records above from existing ones with update_* and linkMode \"add\" " +
                          "(eventRefs, citationHandles, noteHandles), or remove them with delete_object.");
        if (unplaced.Count > 0)
            sb.AppendLine($"  • {string.Join(", ", unplaced.Select(e => e.Where))} " +
                          $"{(unplaced.Count == 1 ? "is" : "are")} in no new family: add to an existing family with " +
                          "update_family (fatherHandle, motherHandle, or childRefs with linkMode \"add\").");
        sb.AppendLine("  • Read a new record with get_object, or a new family's people with get_person_tree.");
        sb.AppendLine("  • To take the whole call back, undo_change on its transaction from get_recent_changes, " +
                      "after the user confirms.");
        return sb.ToString();
    }

    private static string Line(string grampsClass, string label, string handle, string? grampsId, string? key)
    {
        var sb = new StringBuilder($"{grampsClass}: {(string.IsNullOrWhiteSpace(label) ? "(no title)" : label.Trim())}");
        sb.Append($" — handle: {handle}");
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.Append($" | gramps_id: {grampsId}");
        if (key != null)
            sb.Append($" | key: {key}");
        return sb.ToString();
    }

    private static string ExistingLabel(Change change)
    {
        if (change.GrampsClass != "Person" || change.New == null)
            return "";
        try
        {
            return GrampsValueFormatter.FormatName(change.New.Deserialize<GrampsPerson>(GrampsJson.Options)?.PrimaryName);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// <summary>Whether a family of the call names the person as father, mother or child.</summary>
    private static bool InNewFamily(string key, List<Entry> entries) =>
        entries.Where(e => e.Request is CreateFamilyRequest).Select(e => (CreateFamilyRequest)e.Request)
            .Any(f => f.FatherHandle == "#" + key || f.MotherHandle == "#" + key
                      || f.ChildRefList?.Any(c => c.Ref?.Trim() == "#" + key) == true);
}
