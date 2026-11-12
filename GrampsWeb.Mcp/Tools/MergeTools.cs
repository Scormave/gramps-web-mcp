using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool that merges a duplicate record into the record to keep through Gramps Web's merge endpoints,
/// which run Gramps' own merge queries in one transaction.
/// </summary>
[McpServerToolType]
public static class MergeTools
{
    // Gramps TXNDEL in the transaction history.
    private const int Deleted = 2;

    /// <summary>A mergeable type: its objectType, its name in replies, its API collection, and nouns for counts.</summary>
    private sealed record MergeKind(string Type, string DisplayName, string ApiPath, string Noun, string Plural);

    private static readonly IReadOnlyDictionary<string, MergeKind> MergeKinds =
        new Dictionary<string, MergeKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["person"] = new("person", "Person", "people", "person", "people"),
            ["family"] = new("family", "Family", "families", "family", "families"),
            ["event"] = new("event", "Event", "events", "event", "events"),
            ["place"] = new("place", "Place", "places", "place", "places"),
            ["source"] = new("source", "Source", "sources", "source", "sources"),
            ["citation"] = new("citation", "Citation", "citations", "citation", "citations"),
            ["note"] = new("note", "Note", "notes", "note", "notes"),
            ["media"] = new("media", "Media", "media", "media object", "media objects"),
            ["repository"] = new("repository", "Repository", "repositories", "repository", "repositories")
        };

    private const string ValidTypes = "person, family, event, place, source, citation, note, media or repository";

    [McpServerTool(Title = "Merge Objects", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [McpMeta("anthropic/requiresUserInteraction", true)]
    [Description(
        "Merge a duplicate into the record to keep when both describe the same person, family, event, place, source, " +
        "citation, note, media object or repository: Gramps Web moves the duplicate's details and every link to it onto " +
        "the kept record and deletes the duplicate, all in one transaction. Call it only after the user confirmed that " +
        "the two records are the same; read both with get_object first, and find candidates with search or list_objects. " +
        "The kept record keeps its own fields: a person's primary name, gender, birth and death, an event's type, date, " +
        "place and description, a source's title, a citation's page, date and source, a note's text, a media object's " +
        "file. The duplicate's name becomes an alternate name of the kept person or place, and its events, children, " +
        "parents, notes, citations, media, attributes and tags are added. Merging two people also merges a family the " +
        "duplicate had with the same partner (mergeFamilies); merging two families merges their different fathers into " +
        "one person and their different mothers into one person. Gramps refuses to merge spouses or a parent with their " +
        "child, and tags cannot be merged. Clients that support it show the user a confirmation dialog first; action " +
        "declined or cancelled means nothing changed. Returns action merged with the kept and deleted records and the " +
        "transaction number: undo_change with it restores the duplicate.")]
    public static Task<string> MergeObjects(
        [Description("Type of both records: " + ValidTypes + ".")]
        string objectType,
        [Description("The record that stays and keeps its own fields. " + ToolDescriptionFragments.HandleDiscovery)]
        string keepHandle,
        [Description("The duplicate: its details and links move to the kept record, then it is deleted. " + ToolDescriptionFragments.HandleDiscovery)]
        string duplicateHandle,
        [Description("People only: true (default) also merges a family the duplicate had with the same partner as one of the kept person's families; false keeps both families. Ignored for other types.")]
        bool mergeFamilies = true,
        [Description("Families only: the father who stays when the two families name different fathers, who then become one person. The father of either family; default is the kept family's father, or the duplicate's when the kept family has none.")]
        string? fatherHandle = null,
        [Description("Families only: the mother who stays when the two families name different mothers, who then become one person. The mother of either family; default is the kept family's mother, or the duplicate's when the kept family has none.")]
        string? motherHandle = null,
        GrampsApiClient client = null!,
        McpServer server = null!,
        CancellationToken cancellationToken = default) =>
        MergeObjectsAsync(objectType, keepHandle, duplicateHandle, mergeFamilies, fatherHandle, motherHandle, client,
            UserConfirmation.For(server, "merge_objects"), cancellationToken);

    internal static async Task<string> MergeObjectsAsync(string objectType, string keepHandle, string duplicateHandle,
        bool mergeFamilies, string? fatherHandle, string? motherHandle, GrampsApiClient client,
        IUserConfirmation? confirmation = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var kind = Validate(objectType, keepHandle, duplicateHandle, fatherHandle, motherHandle);
            client.EnsureWritable();

            var keep = await HandleResolver.ResolveToHandleAsync(keepHandle.Trim(), client, kind.ApiPath);
            var duplicate = await HandleResolver.ResolveToHandleAsync(duplicateHandle.Trim(), client, kind.ApiPath);
            if (string.Equals(keep, duplicate, StringComparison.Ordinal))
                throw McpToolErrors.ValidationError(
                    "keepHandle and duplicateHandle name the same record; pass two different records.");

            var pair = await ReadPairAsync(client, kind, keep, duplicate, keepHandle, duplicateHandle);
            if (pair.Missing is not null)
                return pair.Missing;
            var (keepData, duplicateData) = (pair.Keep!.Value, pair.Duplicate!.Value);

            if (kind.ApiPath == "people" && PersonRefusal(keepData, duplicateData) is { } refusal)
                return ResponseEnvelope.MergeNotDone(kind.DisplayName, keep, duplicate, "refused",
                    $"Gramps refuses this merge: {refusal} Nothing was changed.");

            var parents = kind.ApiPath == "families"
                ? new FamilyParents(
                    await ChooseParentAsync(client, "father", fatherHandle, ReadString(keepData, "father_handle"),
                        ReadString(duplicateData, "father_handle")),
                    await ChooseParentAsync(client, "mother", motherHandle, ReadString(keepData, "mother_handle"),
                        ReadString(duplicateData, "mother_handle")))
                : null;

            confirmation ??= UserConfirmation.None;
            if (confirmation.Asks)
            {
                // Asks before taking the update lease: the user may take minutes to answer, and this
                // server's other update tools must not wait for them meanwhile.
                var question = await FormatQuestionAsync(client, kind, keep, duplicate, keepData, duplicateData,
                    mergeFamilies, parents);
                var answer = await confirmation.AskAsync(question, cancellationToken);
                if (answer != ConfirmationAnswer.Confirmed)
                    return ResponseEnvelope.MergeNotDone(kind.DisplayName, keep, duplicate,
                        UserConfirmation.ActionName(answer), UserConfirmation.Explain(answer));
            }

            // Holds off this server's other update tools from the check until Gramps Web has the merge.
            using (await client.BeginUpdateAsync())
            {
                if (confirmation.Asks)
                {
                    var current = await ReadPairAsync(client, kind, keep, duplicate, keepHandle, duplicateHandle);
                    if (current.Missing is not null)
                        return current.Missing;
                    if (Digest(current.Keep!.Value, current.Duplicate!.Value) != Digest(keepData, duplicateData))
                    {
                        return ResponseEnvelope.MergeNotDone(kind.DisplayName, keep, duplicate, "refused",
                            "One of the two records changed while the user was deciding, so nothing was merged. " +
                            "Read both again and ask the user before calling again.");
                    }
                }

                try
                {
                    await client.PostActionAsync(
                        $"/api/{kind.ApiPath}/{Uri.EscapeDataString(keep)}/merge/{Uri.EscapeDataString(duplicate)}",
                        RequestBody(kind, mergeFamilies, parents));
                }
                catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    return ResponseEnvelope.MergeNotDone(kind.DisplayName, keep, duplicate, "refused",
                        $"Gramps refuses this merge: {ServerMessage(ex.ResponseBody) ?? ex.Message} Nothing was changed.");
                }
                catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound
                                                    && ServerMessage(ex.ResponseBody)?.Contains("handle not found",
                                                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    // "Phoenix handle not found" or "Titanic handle not found".
                    return ResponseEnvelope.MergeNotDone(kind.DisplayName, keep, duplicate, "refused",
                        "One of the two records was deleted meanwhile, so nothing was merged. Read both again " +
                        "before calling again.");
                }
                catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException(
                        "Gramps Web refused the merge: it needs permission to edit and to delete records, which " +
                        "this account lacks. Nothing was changed.", ex);
                }
            }

            var transaction = await FindMergeTransactionAsync(client, duplicate);
            return FormatMerged(kind, keep, duplicate, keepData, duplicateData, mergeFamilies, transaction);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static MergeKind Validate(string objectType, string keepHandle, string duplicateHandle,
        string? fatherHandle, string? motherHandle)
    {
        var type = objectType?.Trim() ?? "";
        if (string.Equals(type, "tag", StringComparison.OrdinalIgnoreCase))
            throw McpToolErrors.ValidationError(
                "Tags cannot be merged: Gramps Web has no merge for tags. Add the kept tag to the records of the other " +
                "with their update tools (tagHandles, linkMode add), then delete the other tag with delete_records.");
        if (!MergeKinds.TryGetValue(type, out var kind))
            throw McpToolErrors.ValidationError($"Invalid objectType. Must be one of: {ValidTypes}.");
        if (string.IsNullOrWhiteSpace(keepHandle) || string.IsNullOrWhiteSpace(duplicateHandle))
            throw McpToolErrors.ValidationError("keepHandle and duplicateHandle are both required.");
        if (kind.ApiPath != "families" && (!string.IsNullOrWhiteSpace(fatherHandle) || !string.IsNullOrWhiteSpace(motherHandle)))
            throw McpToolErrors.ValidationError("fatherHandle and motherHandle apply only to objectType family.");
        return kind;
    }

    private sealed record Pair(JsonElement? Keep, JsonElement? Duplicate, string? Missing);

    /// <summary>Both records, or the not-found message of the first one missing.</summary>
    private static async Task<Pair> ReadPairAsync(GrampsApiClient client, MergeKind kind, string keep,
        string duplicate, string keepIdentifier, string duplicateIdentifier)
    {
        var keepTask = ReadAsync(client, kind, keep);
        var duplicateTask = ReadAsync(client, kind, duplicate);
        await Task.WhenAll(keepTask, duplicateTask);
        if (await keepTask is not { } keepData)
            return new Pair(null, null, NotFoundHelper.NotFoundMessage(kind.DisplayName, keepIdentifier.Trim()));
        if (await duplicateTask is not { } duplicateData)
            return new Pair(null, null, NotFoundHelper.NotFoundMessage(kind.DisplayName, duplicateIdentifier.Trim()));
        return new Pair(keepData, duplicateData, null);
    }

    private static async Task<JsonElement?> ReadAsync(GrampsApiClient client, MergeKind kind, string handle)
    {
        var payload = await client.GetJsonOrNullIfNotFoundAsync($"/api/{kind.ApiPath}/{Uri.EscapeDataString(handle)}");
        return payload is { ValueKind: JsonValueKind.Object } ? payload : null;
    }

    /// <summary>The last change times of both records: what the user saw when confirming.</summary>
    private static string Digest(JsonElement keep, JsonElement duplicate) =>
        (keep.TryGetProperty("change", out var a) ? a.GetRawText() : "") + "|" +
        (duplicate.TryGetProperty("change", out var b) ? b.GetRawText() : "");

    /// <summary>Gramps' own refusals of MergePersonQuery, checked before the user is asked.</summary>
    private static string? PersonRefusal(JsonElement keep, JsonElement duplicate)
    {
        var keepFamilies = ReadHandles(keep, "family_list");
        var duplicateFamilies = ReadHandles(duplicate, "family_list");
        if (keepFamilies.Overlaps(duplicateFamilies))
            return "the two people are partners in the same family, and spouses cannot be merged. If they are really " +
                   "one person, remove one of them from that family with update_family first.";
        if (keepFamilies.Overlaps(ReadHandles(duplicate, "parent_family_list"))
            || duplicateFamilies.Overlaps(ReadHandles(keep, "parent_family_list")))
            return "one of the two people is a child in a family of the other, and a parent and child cannot be " +
                   "merged. If they are really one person, remove the wrong link with update_family first.";
        return null;
    }

    /// <summary>The parent a merged family keeps, and the other parent Gramps merges into them.</summary>
    private sealed record Parent(string Role, string? Kept, string? Merged);

    private sealed record FamilyParents(Parent Father, Parent Mother);

    /// <summary>
    /// The parent the merged family keeps: <paramref name="requested"/>, which must be the parent of one of the
    /// families, or else the kept family's, or the duplicate's when the kept family has none. Gramps cannot
    /// keep a missing parent while deleting a present one.
    /// </summary>
    private static async Task<Parent> ChooseParentAsync(GrampsApiClient client, string role, string? requested,
        string? ofKeep, string? ofDuplicate)
    {
        string? kept;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            kept = await HandleResolver.ResolveToHandleAsync(requested.Trim(), client, "people");
            if (kept != ofKeep && kept != ofDuplicate)
                throw McpToolErrors.ValidationError(
                    $"{role}Handle must be the {role} of one of the two families; {requested.Trim()} is neither. " +
                    $"Read both families with get_object to see their {role}s.");
        }
        else
        {
            kept = ofKeep ?? ofDuplicate;
        }

        var other = kept == ofKeep ? ofDuplicate : ofKeep;
        return new Parent(role, kept, other is not null && other != kept ? other : null);
    }

    private static string RequestBody(MergeKind kind, bool mergeFamilies, FamilyParents? parents)
    {
        var body = new JsonObject();
        if (kind.ApiPath == "people")
            body["family_merger"] = mergeFamilies;
        if (parents?.Father.Kept is { } father)
            body["phoenix_father_handle"] = father;
        if (parents?.Mother.Kept is { } mother)
            body["phoenix_mother_handle"] = mother;
        return body.ToJsonString();
    }

    /// <summary>
    /// "Merge Person I0042 into I0007?" with both records' labels, what the kept record keeps and gains, and for
    /// families the parents that become one person; plain text, as clients show the question as it is.
    /// </summary>
    private static async Task<string> FormatQuestionAsync(GrampsApiClient client, MergeKind kind, string keep,
        string duplicate, JsonElement keepData, JsonElement duplicateData, bool mergeFamilies, FamilyParents? parents)
    {
        var mergedParents = new[] { parents?.Father, parents?.Mother }
            .Where(p => p?.Kept is not null && p.Merged is not null)
            .Select(p => p!)
            .ToList();
        var labels = await LoadLabelsAsync(client, kind, [keep, duplicate],
            mergedParents.SelectMany(p => new[] { p.Kept!, p.Merged! }).ToList());

        var keepId = ReadString(keepData, "gramps_id");
        var duplicateId = ReadString(duplicateData, "gramps_id");
        var sb = new StringBuilder($"Merge {kind.DisplayName} {duplicateId ?? $"[handle: {duplicate}]"} into ");
        sb.Append(keepId ?? $"[handle: {keep}]").AppendLine("?");
        sb.AppendLine();
        sb.AppendLine($"  • Keep {Named(keepId, keep, labels)}");
        sb.AppendLine($"  • Merge into it and delete {Named(duplicateId, duplicate, labels)}");
        sb.AppendLine();
        sb.AppendLine(WhatSurvives(kind, mergeFamilies));
        foreach (var parent in mergedParents)
        {
            sb.AppendLine($"The two {parent.Role}s become one person: {Label(parent.Kept!, labels)} stays, and " +
                          $"{Label(parent.Merged!, labels)} is merged into that person and deleted.");
        }

        sb.AppendLine();
        sb.Append("The change history keeps the duplicate: undo_change can undo the merge later.");
        return sb.ToString();
    }

    private static string Named(string? grampsId, string handle, IReadOnlyDictionary<string, string> labels) =>
        (grampsId ?? $"[handle: {handle}]") + (labels.TryGetValue(handle, out var label) ? $": {label}" : "");

    private static string Label(string handle, IReadOnlyDictionary<string, string> labels) =>
        labels.TryGetValue(handle, out var label) ? label : $"[handle: {handle}]";

    /// <summary>Labels of the two records and of the people named with them; without them the question still asks.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> LoadLabelsAsync(GrampsApiClient client,
        MergeKind kind, IReadOnlyList<string> records, IReadOnlyList<string> people)
    {
        try
        {
            return await LinkedObjectLabels.LoadAsync(client,
                [(kind.ApiPath, records), ("people", people)]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>What Gramps' merge keeps from each record, by type (Gramps 6.0 <c>merge</c> methods).</summary>
    private static string WhatSurvives(MergeKind kind, bool mergeFamilies) => kind.ApiPath switch
    {
        "people" =>
            "The kept person keeps their primary name, gender, birth and death; the duplicate's name becomes an " +
            "alternate name, its Gramps ID goes into a \"Merged Gramps ID\" attribute, and its events, parents, " +
            "families, notes, citations, media and other details move to the kept person. " +
            (mergeFamilies
                ? "A family the duplicate had with the same partner as the kept person is merged too."
                : "Families with the same partner stay separate (mergeFamilies is false)."),
        "families" =>
            "The kept family keeps its relationship type unless it is Unknown; the duplicate's children, events, " +
            "notes, citations, media and attributes move to it.",
        "events" =>
            "The kept event keeps its type, date, place and description; the duplicate's are dropped. Its notes, " +
            "citations, media and attributes move to the kept event, and everyone who took part in it now takes " +
            "part in the kept one.",
        "places" =>
            "The kept place keeps its name, type, coordinates and code; the duplicate's names become alternate " +
            "names, and its enclosing places, notes, citations, media and URLs move to the kept place, as do the " +
            "events that happened there.",
        "sources" =>
            "The kept source keeps its title, author, publication info and abbreviation; the duplicate's notes, " +
            "media, attributes and repository references move to it, and the duplicate's citations now cite it.",
        "citations" =>
            "The kept citation keeps its page, date and source; the confidence becomes the more extreme of the two, " +
            "and the duplicate's notes, media and attributes move to it, as do the records that cited the duplicate.",
        "notes" =>
            "The kept note keeps its text and type; the duplicate's text is dropped, and the records that showed " +
            "the duplicate now show the kept note.",
        "media" =>
            "The kept media object keeps its file, date and description; the duplicate's file is no longer used " +
            "by the tree, and its notes, citations and attributes move to the kept one, as do the records that " +
            "showed it.",
        "repositories" =>
            "The kept repository keeps its name and type; the duplicate's addresses, URLs and notes move to it, " +
            "and the sources held there now name the kept repository.",
        _ => throw new InvalidOperationException("Validated object type has no merge description.")
    };

    /// <summary>
    /// The merge's transaction: the newest of the latest that deleted the duplicate. Null when the history
    /// cannot be read.
    /// </summary>
    private static async Task<GrampsTransaction?> FindMergeTransactionAsync(GrampsApiClient client, string duplicate)
    {
        try
        {
            var latest = await client.GetPagedListAsync<GrampsTransaction>(
                "/api/transactions/history/?page=1&pagesize=10&sort=-id");
            return (latest.Objects ?? [])
                .OrderByDescending(t => t.Id)
                .FirstOrDefault(t => (t.Changes ?? []).Any(c => c.TransType == Deleted
                                                                && c.ObjHandle?.Trim() == duplicate));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // The merge is done; the reply then points to get_recent_changes.
        }
    }

    private static string FormatMerged(MergeKind kind, string keep, string duplicate, JsonElement keepData,
        JsonElement duplicateData, bool mergeFamilies, GrampsTransaction? transaction)
    {
        var keepId = ReadString(keepData, "gramps_id");
        var duplicateId = ReadString(duplicateData, "gramps_id");
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {kind.DisplayName}");
        sb.AppendLine("action: merged");
        sb.AppendLine($"handle: {keep}");
        if (keepId is not null)
            sb.AppendLine($"gramps_id: {keepId}");
        sb.AppendLine($"duplicate_handle: {duplicate}");
        if (duplicateId is not null)
            sb.AppendLine($"duplicate_gramps_id: {duplicateId}");
        if (transaction is not null)
            sb.AppendLine($"transaction: {transaction.Id}");
        sb.AppendLine("---");

        var keepName = keepId ?? keep;
        sb.AppendLine($"{duplicateId ?? duplicate} was merged into {keepName} and deleted; the records that linked " +
                      $"to it now link to {keepName}.");
        if (AlsoDeleted(transaction, duplicate) is { Length: > 0 } also)
            sb.AppendLine($"The same merge also joined and deleted {also}.");

        sb.AppendLine();
        sb.AppendLine("Next steps:");
        foreach (var step in NextSteps(kind, keep, keepData, duplicateData, mergeFamilies, transaction))
            sb.AppendLine($"  • {step}");
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>"1 family" or "1 person and 1 family": records other than the duplicate the merge deleted.</summary>
    private static string AlsoDeleted(GrampsTransaction? transaction, string duplicate)
    {
        var counts = (transaction?.Changes ?? [])
            .Where(c => c.TransType == Deleted && c.ObjHandle?.Trim() is { Length: > 0 } h && h != duplicate
                        && MergeKinds.Values.Any(k => k.DisplayName == c.ObjClass?.Trim()))
            .GroupBy(c => MergeKinds.Values.First(k => k.DisplayName == c.ObjClass!.Trim()))
            .Select(g => g.Select(c => c.ObjHandle).Distinct().Count() is var n && n == 1
                ? $"1 {g.Key.Noun}"
                : $"{n} {g.Key.Plural}")
            .ToList();
        return counts.Count switch
        {
            0 => "",
            1 => counts[0],
            _ => string.Join(", ", counts[..^1]) + " and " + counts[^1]
        };
    }

    private static IEnumerable<string> NextSteps(MergeKind kind, string keep, JsonElement keepData,
        JsonElement duplicateData, bool mergeFamilies, GrampsTransaction? transaction)
    {
        if (kind.ApiPath is "people" or "families")
        {
            yield return $"Read the result: get_object(objectType: \"{kind.Type}\", identifier: \"{keep}\", extended: true). " +
                         (kind.ApiPath == "people"
                             ? "Two Birth or two Death events mean the duplicate's came along: "
                             : "Two Marriage events mean the duplicate's came along: ") +
                         "merge them with merge_objects(objectType: \"event\", …) once the user confirms.";
        }
        else
        {
            yield return $"Read the result: get_object(objectType: \"{kind.Type}\", identifier: \"{keep}\").";
        }

        if (kind.ApiPath == "people" && ReadHandles(keepData, "parent_family_list").Count > 0
                                     && ReadHandles(duplicateData, "parent_family_list").Count > 0)
        {
            yield return "Both people had parents, so the kept person now has two parent families; if they are the " +
                         "same parents, merge the families with merge_objects(objectType: \"family\", …).";
        }

        if (kind.ApiPath == "people" && !mergeFamilies && ReadHandles(keepData, "family_list").Count > 0
            && ReadHandles(duplicateData, "family_list").Count > 0)
        {
            yield return "Families with the same partner were kept apart; merge them with " +
                         "merge_objects(objectType: \"family\", …) if they are one marriage.";
        }

        yield return transaction is not null
            ? $"To undo the merge after the user confirms: undo_change(transactionId: {transaction.Id})."
            : "To undo the merge, find it in get_recent_changes and pass its transaction to undo_change after the user confirms.";
    }

    private static HashSet<string> ReadHandles(JsonElement data, string name) =>
        data.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null)
                .Where(h => !string.IsNullOrEmpty(h))
                .Select(h => h!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static string? ReadString(JsonElement data, string name) =>
        data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                                                 && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;

    /// <summary>The <c>error.message</c> of a Gramps Web error reply.</summary>
    private static string? ServerMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("message", out var message)
                   && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
