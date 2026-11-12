using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Deletes records of any types through Gramps Web's <c>POST /api/objects/delete-by-handle/</c>, which deletes
/// handles of one type in one transaction and removes the links to them from other records. The records of a
/// call are checked and confirmed together; then each type is deleted in its own transaction.
/// </summary>
internal static class DeleteHelper
{
    /// <summary>Records one call deletes at most.</summary>
    internal const int MaxRecords = 50;

    /// <summary>Records that link to the deleted ones, named in the confirmation question.</summary>
    internal const int MaxBacklinksNamed = 5;

    internal const string DeletePath = "/api/objects/delete-by-handle/";

    /// <summary>
    /// The order of the per-type transactions: records go before the records they link to, so Gramps Web does
    /// not first update a record to drop a link to another record of the call. Families go before people, as
    /// Gramps Web deletes a family whose last member it deletes, and a family of the call is then deleted
    /// in its own transaction.
    /// </summary>
    internal static readonly IReadOnlyList<string> Order =
        ["families", "people", "events", "places", "media", "citations", "sources", "repositories", "notes", "tags"];

    private const string ProbeQuery = "keys=handle,gramps_id,change,backlinks&backlinks=true";

    /// <summary>A record to delete: its collection (such as <c>people</c>), handle, and the identifier given.</summary>
    internal sealed record Target(string Collection, string Handle, string Identifier);

    /// <summary>
    /// Deletes <paramref name="targets"/>, one transaction per type. Nothing is deleted when a record is
    /// missing, when records outside the call link to one without <paramref name="force"/>, when a place
    /// encloses places outside the call, or when the user does not confirm. When
    /// <paramref name="confirmation"/> asks, the records are read again after the answer, so records changed
    /// or relinked while the user was deciding are not deleted.
    /// </summary>
    public static async Task<string> DeleteAsync(GrampsApiClient client, IReadOnlyList<Target> targets, bool force,
        IUserConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        client.EnsureWritable();
        var records = await ReadAsync(client, targets);
        if (targets.Where(t => !records.ContainsKey(Key(t))).ToList() is { Count: > 0 } missing)
            return NotFound(targets, missing);

        var inCall = targets.Select(Key).ToHashSet();
        var outside = targets
            .Select(t => (Target: t, Links: records[Key(t)].Links.Where(l => !inCall.Contains(l)).ToList()))
            .Where(x => x.Links.Count > 0)
            .ToList();

        var enclosing = outside
            .Where(x => x.Target.Collection == "places")
            .Select(x => (x.Target, Places: x.Links.Where(l => l.Collection == "places").ToList()))
            .Where(x => x.Places.Count > 0)
            .ToList();
        if (enclosing.Count > 0)
            return await EnclosedPlacesRefusalAsync(client, targets, records, enclosing);

        var effects = Effects.Of(outside);
        if (!force && outside.Count > 0)
            return LinkedRefusal(targets, records, outside, effects);

        var labels = await LoadLabelsAsync(client,
            targets.Select(Key).Concat(effects.Unlinked.Take(MaxBacklinksNamed)));
        if (confirmation.Asks)
        {
            var question = FormatQuestion(targets, records, labels, effects);
            var answer = await confirmation.AskAsync(question, cancellationToken);
            if (answer != ConfirmationAnswer.Confirmed)
                return NotDone(targets.Count, UserConfirmation.ActionName(answer), UserConfirmation.Explain(answer));

            var current = await ReadAsync(client, targets);
            if (targets.Any(t => !current.TryGetValue(Key(t), out var now) || now.Digest != records[Key(t)].Digest))
            {
                return NotDone(targets.Count, "refused",
                    "The records or the records linking to them changed while the user was deciding, so nothing " +
                    "was deleted. Read them again and ask the user before calling again.");
            }
        }

        var deleted = new List<Target>();
        var changes = new List<Change>();
        var transactions = 0;
        foreach (var group in Order.Select(c => targets.Where(t => t.Collection == c).ToList()).Where(g => g.Count > 0))
        {
            string body;
            try
            {
                (_, body) = await client.PostActionAsync(DeletePath, new JsonObject
                {
                    ["namespace"] = group[0].Collection,
                    ["handles"] = new JsonArray(group.Select(t => (JsonNode)JsonValue.Create(t.Handle)).ToArray())
                }.ToJsonString());
            }
            catch (Exception ex) when (deleted.Count > 0)
            {
                return PartiallyDeleted(targets, deleted, records, labels, group[0].Collection, ex);
            }

            transactions++;
            deleted.AddRange(group);
            changes.AddRange(ParseChanges(body));
        }

        return await DeletedAsync(client, targets, records, labels, changes, transactions);
    }

    private static (string Collection, string Handle) Key(Target target) => (target.Collection, target.Handle);

    /// <summary>The record as the check reads it: its Gramps ID, what the user saw, and the records linking to it.</summary>
    private sealed record Stored(string? GrampsId, string Digest, IReadOnlyList<(string Collection, string Handle)> Links)
    {
        public static Stored From(Probe probe)
        {
            var links = (probe.Backlinks ?? [])
                .Where(pair => SearchFormatter.CollectionOf(pair.Key) != null)
                .SelectMany(pair => (pair.Value ?? [])
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => (Collection: SearchFormatter.CollectionOf(pair.Key)!, Handle: h.Trim())))
                .Distinct()
                .ToList();
            var digest = $"{probe.Change}|" + string.Join(";", links
                .Select(l => $"{l.Collection}/{l.Handle}")
                .Order(StringComparer.Ordinal));
            return new Stored(probe.GrampsId?.Trim() is { Length: > 0 } id ? id : null, digest, links);
        }
    }

    private sealed class Probe
    {
        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        [JsonPropertyName("gramps_id")]
        public string? GrampsId { get; set; }

        [JsonPropertyName("change")]
        public long? Change { get; set; }

        /// <summary>Handles by lower-case class, such as <c>"person": [handles]</c>.</summary>
        [JsonPropertyName("backlinks")]
        public Dictionary<string, string[]?>? Backlinks { get; set; }
    }

    /// <summary>The targets that exist, with their backlinks: one batch request per type.</summary>
    private static async Task<Dictionary<(string Collection, string Handle), Stored>> ReadAsync(
        GrampsApiClient client, IReadOnlyList<Target> targets)
    {
        var found = await Task.WhenAll(targets.GroupBy(t => t.Collection).Select(async group => (
            Collection: group.Key,
            Records: await client.GetByHandlesAsync<Probe>(group.Key, group.Select(t => t.Handle), p => p.Handle,
                ProbeQuery))));
        return found
            .SelectMany(g => g.Records.Select(r => (Key: (g.Collection, r.Key), Value: Stored.From(r.Value))))
            .ToDictionary(r => r.Key, r => r.Value);
    }

    /// <summary>What deleting with force does beyond removing links: records Gramps Web deletes along.</summary>
    private sealed record Effects(
        IReadOnlyList<(string Collection, string Handle)> Unlinked,
        int CitationsOfSources,
        bool FamiliesOfPeople)
    {
        public static Effects Of(IReadOnlyList<(Target Target, List<(string Collection, string Handle)> Links)> outside)
        {
            var citations = outside
                .Where(x => x.Target.Collection == "sources")
                .SelectMany(x => x.Links.Where(l => l.Collection == "citations"))
                .ToHashSet();
            return new Effects(
                outside.SelectMany(x => x.Links).Distinct().Where(l => !citations.Contains(l)).ToList(),
                citations.Count,
                outside.Any(x => x.Target.Collection == "people" && x.Links.Any(l => l.Collection == "families")));
        }

        /// <summary>Lines on what Gramps Web deletes along with the records; empty when nothing.</summary>
        public IEnumerable<string> Notes()
        {
            if (CitationsOfSources > 0)
                yield return $"Gramps Web also deletes {Count(CitationsOfSources, "citation")} " +
                             (CitationsOfSources == 1 ? "that cites" : "that cite") + " a deleted source.";
            if (FamiliesOfPeople)
                yield return "Gramps Web also deletes a family left without father, mother and children.";
        }
    }

    private static string NotFound(IReadOnlyList<Target> targets, IReadOnlyList<Target> missing)
    {
        var messages = missing.Select(t => NotFoundHelper.NotFoundMessage(ClassOf(t.Collection), t.Identifier));
        return targets.Count == 1
            ? messages.Single()
            : $"Nothing was deleted: {missing.Count} of {targets.Count} records were not found.\n\n" +
              string.Join("\n\n", messages);
    }

    private static string LinkedRefusal(IReadOnlyList<Target> targets,
        IReadOnlyDictionary<(string, string), Stored> records,
        IReadOnlyList<(Target Target, List<(string Collection, string Handle)> Links)> outside, Effects effects)
    {
        var sb = new StringBuilder();
        sb.AppendLine(targets.Count == 1
            ? "Nothing was deleted: other records link to it."
            : $"Nothing was deleted: other records link to {outside.Count} of the {targets.Count} records.");
        foreach (var (target, links) in outside)
        {
            sb.Append("  • ").Append(Name(target, records[Key(target)].GrampsId, null))
                .Append(": linked from ")
                .AppendLine(string.Join(", ", links.GroupBy(l => l.Collection).Select(g => $"{g.Key}: {g.Count()}")));
        }

        sb.AppendLine();
        sb.Append("Unlink them first with the linking records' update tools (linkMode \"remove\"), add those records " +
                  "to this call, or pass force=true once the user confirmed deleting records that others still link " +
                  "to: Gramps Web then removes those links.");
        foreach (var note in effects.Notes())
            sb.Append(' ').Append(note);
        return NotDone(targets.Count, "refused", sb.ToString());
    }

    private static async Task<string> EnclosedPlacesRefusalAsync(GrampsApiClient client, IReadOnlyList<Target> targets,
        IReadOnlyDictionary<(string, string), Stored> records,
        IReadOnlyList<(Target Target, List<(string Collection, string Handle)> Places)> enclosing)
    {
        var labels = await LoadLabelsAsync(client, enclosing.SelectMany(x => x.Places));
        var sb = new StringBuilder();
        sb.AppendLine("Nothing was deleted: Gramps Web would leave the places that a deleted place encloses " +
                      "pointing at a missing place.");
        foreach (var (target, places) in enclosing)
        {
            sb.Append("  • ").Append(Name(target, records[Key(target)].GrampsId, null)).AppendLine(" encloses:");
            foreach (var (_, handle) in places)
                sb.Append("      ").AppendLine(Line("places", labels.GetValueOrDefault(handle), handle, null));
        }

        sb.AppendLine();
        sb.Append("Change their enclosedBy with update_place, or add them to this call. force does not change this.");
        return NotDone(targets.Count, "refused", sb.ToString());
    }

    /// <summary>
    /// "Delete Note N0001: [General] A note?" for one record, or "Delete 3 records?" with a line per record,
    /// then what Gramps Web changes besides, in plain text: clients show the question as it is.
    /// </summary>
    private static string FormatQuestion(IReadOnlyList<Target> targets,
        IReadOnlyDictionary<(string, string), Stored> records, IReadOnlyDictionary<string, string> labels,
        Effects effects)
    {
        var sb = new StringBuilder();
        if (targets.Count == 1)
        {
            var target = targets[0];
            sb.Append("Delete ").Append(Name(target, records[Key(target)].GrampsId, labels.GetValueOrDefault(target.Handle)))
                .AppendLine("?");
        }
        else
        {
            sb.AppendLine($"Delete {targets.Count} records?");
            foreach (var target in targets)
            {
                sb.Append("  • ")
                    .AppendLine(Name(target, records[Key(target)].GrampsId, labels.GetValueOrDefault(target.Handle)));
            }
        }

        var them = targets.Count == 1 ? "it" : "them";
        if (effects.Unlinked.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(effects.Unlinked.Count == 1
                ? $"1 other record links to {them}; Gramps Web removes that link:"
                : $"{effects.Unlinked.Count} other records link to {them}; Gramps Web removes those links:");
            foreach (var (collection, handle) in effects.Unlinked.Take(MaxBacklinksNamed))
            {
                sb.Append("  • ").Append(ClassOf(collection));
                sb.AppendLine(labels.TryGetValue(handle, out var label) ? $": {label}" : $" [handle: {handle}]");
            }
            if (effects.Unlinked.Count > MaxBacklinksNamed)
                sb.AppendLine($"  • … and {effects.Unlinked.Count - MaxBacklinksNamed} more");
        }

        if (effects.Notes().ToList() is { Count: > 0 } notes)
        {
            sb.AppendLine();
            foreach (var note in notes)
                sb.AppendLine(note);
        }

        sb.AppendLine();
        sb.Append($"The change history keeps {them}: undo_change can restore {them} later.");
        return sb.ToString();
    }

    /// <summary>Labels by handle; without them the question still asks and the reply still lists handles.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> LoadLabelsAsync(GrampsApiClient client,
        IEnumerable<(string Collection, string Handle)> records)
    {
        try
        {
            return await LinkedObjectLabels.LoadAsync(client, records
                .GroupBy(r => r.Collection)
                .Select(group => (group.Key, (IEnumerable<string>?)group.Select(r => r.Handle).ToList())));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>One record of a transaction: what was done to it and the record before and after.</summary>
    private sealed record Change(string Collection, string Handle, string Type, JsonElement? Old, JsonElement? New);

    /// <summary>The changes of a delete-by-handle reply; an unexpected body gives none.</summary>
    private static IEnumerable<Change> ParseChanges(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return doc.RootElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => (
                    Collection: SearchFormatter.CollectionOf(String(item, "_class")),
                    Handle: String(item, "handle"),
                    Type: String(item, "type"),
                    Old: Object(item, "old"),
                    New: Object(item, "new")))
                .Where(c => c.Collection != null && c.Handle != null && c.Type != null)
                .Select(c => new Change(c.Collection!, c.Handle!, c.Type!, c.Old, c.New))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }

        static string? String(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static JsonElement? Object(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value.Clone() : null;
    }

    private static async Task<string> DeletedAsync(GrampsApiClient client, IReadOnlyList<Target> targets,
        IReadOnlyDictionary<(string, string), Stored> records, IReadOnlyDictionary<string, string> labels,
        IReadOnlyList<Change> changes, int transactions)
    {
        var inCall = targets.Select(Key).ToHashSet();
        // A record can be updated and then deleted in one transaction; its last state counts.
        var final = changes
            .GroupBy(c => (c.Collection, c.Handle))
            .Where(g => !inCall.Contains(g.Key))
            .Select(g => g.FirstOrDefault(c => c.Type == "delete") ?? g.Last())
            .ToList();
        var alsoDeleted = final.Where(c => c.Type == "delete").ToList();
        var unlinked = final.Where(c => c.Type == "update").ToList();
        var stored = await StoredLabelsAsync(client, alsoDeleted.Concat(unlinked));

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Transaction");
        sb.AppendLine("action: deleted");
        sb.AppendLine($"records: {targets.Count}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(transactions == 1
            ? $"Deleted {Count(targets.Count, "record")} in one transaction: get_recent_changes lists it, and " +
              "undo_change on it restores what it deleted."
            : $"Deleted {Count(targets.Count, "record")} in {transactions} transactions, one per record type: " +
              "get_recent_changes lists them, and undo_change on each, newest first, restores what it deleted.");
        sb.AppendLine();
        foreach (var target in targets)
        {
            sb.AppendLine(Line(target.Collection, labels.GetValueOrDefault(target.Handle), target.Handle,
                records[Key(target)].GrampsId));
        }

        AppendChanges(sb, $"Also deleted by Gramps Web ({alsoDeleted.Count}):", alsoDeleted, stored);
        AppendChanges(sb, $"Links to them removed from ({unlinked.Count}):", unlinked, stored);
        return sb.ToString().TrimEnd();
    }

    private static void AppendChanges(StringBuilder sb, string title, IReadOnlyList<Change> changes,
        IReadOnlyDictionary<string, string> labels)
    {
        if (changes.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine(title);
        foreach (var change in changes)
        {
            var data = change.Type == "delete" ? change.Old : change.New;
            sb.AppendLine(Line(change.Collection, labels.GetValueOrDefault(change.Handle), change.Handle,
                data is { } d && d.TryGetProperty("gramps_id", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null));
        }
    }

    /// <summary>Labels of changed records from the stored data the reply carries, so nothing is read again.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> StoredLabelsAsync(GrampsApiClient client,
        IEnumerable<Change> changes)
    {
        var list = changes.ToList();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (list.Count == 0)
            return labels;
        try
        {
            var tables = await GrampsDefaultTypeLabels.PrefetchForSearchAsync(list.Select(c => c.Collection), client);
            foreach (var change in list)
            {
                if ((change.Type == "delete" ? change.Old : change.New) is { } data
                    && StoredRecordLabels.Label(change.Collection, data, tables) is { } label)
                    labels[change.Handle] = label;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The records keep their handles and IDs.
        }

        return labels;
    }

    private static string PartiallyDeleted(IReadOnlyList<Target> targets, IReadOnlyList<Target> deleted,
        IReadOnlyDictionary<(string, string), Stored> records, IReadOnlyDictionary<string, string> labels,
        string failedCollection, Exception error)
    {
        var done = deleted.Select(Key).ToHashSet();
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Transaction");
        sb.AppendLine("action: partially_deleted");
        sb.AppendLine($"records: {deleted.Count}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Deleted {deleted.Count} of {targets.Count} records; deleting the {failedCollection} failed, " +
                      $"so it and the types after it were not deleted: {error.Message.Trim()}");
        sb.AppendLine("get_recent_changes lists the transactions that were made; undo_change on each restores what " +
                      "it deleted. Tell the user before calling again for the rest.");
        sb.AppendLine();
        sb.AppendLine("Deleted:");
        foreach (var target in targets.Where(t => done.Contains(Key(t))))
            sb.AppendLine(Line(target.Collection, labels.GetValueOrDefault(target.Handle), target.Handle,
                records[Key(target)].GrampsId));
        sb.AppendLine();
        sb.AppendLine("Not deleted:");
        foreach (var target in targets.Where(t => !done.Contains(Key(t))))
            sb.AppendLine(Line(target.Collection, labels.GetValueOrDefault(target.Handle), target.Handle,
                records[Key(target)].GrampsId));
        return sb.ToString().TrimEnd();
    }

    /// <summary>A delete that did not happen: <paramref name="action"/> such as <c>declined</c>, then why.</summary>
    private static string NotDone(int records, string action, string reason) =>
        $"---\ntype: Transaction\naction: {action}\nrecords: {records}\n---\n{reason}";

    /// <summary>"Note N0001: [General] A note", "Note N0001", or "Note [handle: h]" without a Gramps ID.</summary>
    private static string Name(Target target, string? grampsId, string? label)
    {
        var sb = new StringBuilder(ClassOf(target.Collection));
        sb.Append(grampsId is null ? $" [handle: {target.Handle}]" : $" {grampsId}");
        if (!string.IsNullOrWhiteSpace(label))
            sb.Append(": ").Append(label.Trim());
        return sb.ToString();
    }

    /// <summary>"Person: label — handle: h | gramps_id: I0001", the record lines of create_records.</summary>
    private static string Line(string collection, string? label, string handle, string? grampsId)
    {
        var sb = new StringBuilder(ClassOf(collection));
        if (!string.IsNullOrWhiteSpace(label))
            sb.Append(": ").Append(label.Trim());
        sb.Append(" — handle: ").Append(handle);
        if (!string.IsNullOrWhiteSpace(grampsId))
            sb.Append(" | gramps_id: ").Append(grampsId.Trim());
        return sb.ToString();
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    internal static string ClassOf(string collection) => collection switch
    {
        "people" => "Person",
        "families" => "Family",
        "events" => "Event",
        "places" => "Place",
        "sources" => "Source",
        "citations" => "Citation",
        "notes" => "Note",
        "media" => "Media",
        "repositories" => "Repository",
        "tags" => "Tag",
        _ => "Record"
    };
}
