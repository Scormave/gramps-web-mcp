using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool that reverts one transaction of the tree's edit history through Gramps Web's undo endpoint.
/// </summary>
[McpServerToolType]
public static partial class UndoTools
{
    // Gramps TXNADD, TXNUPD and TXNDEL; the history logs links as changes of REFERENCE_KEY, sent as "7".
    private const int Added = 0;
    private const int Updated = 1;
    private const int Deleted = 2;
    private const string ReferenceClass = "7";

    /// <summary>Past this many records the link checks are skipped; Gramps Web's own check still runs.</summary>
    internal const int MaxCheckedRecords = 200;

    /// <summary>Records named in a conflict's list of links.</summary>
    private const int MaxLinksNamed = 5;

    private const int MaxDescriptionInMessage = 80;

    /// <summary>Gramps Web 3.23 restores deleted records without force (gramps-web-api #1006).</summary>
    private static readonly Version DeletionUndoFixed = new(3, 23);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Stays under the usual 60-second tool-call timeout of MCP clients.</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(45);

    [McpServerTool(Title = "Undo Change", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [McpMeta("anthropic/requiresUserInteraction", true)]
    [Description(
        "Undo one transaction listed by get_recent_changes: records it added are deleted, records it updated get " +
        "their earlier version back, and records it deleted are restored with the same handles and Gramps IDs. " +
        "The undo is one new all-or-nothing transaction, which get_recent_changes lists and undo_change can revert in turn. " +
        "Call it only after the user confirmed that transaction; dryRun lists the effects and conflicts without changing " +
        "anything. A conflict stops the undo with nothing changed: a record changed again later, a record to be deleted " +
        "that other records now link to, or an earlier version linking a record that no longer exists. Undo the later " +
        "transactions first, newest first, or pass force=true after a separate confirmation. Clients that support it " +
        "also show the user a confirmation dialog before undoing; action declined or cancelled means the user said no. " +
        "Returns action undone, checked, refused or pending, the records affected and the undo's own transaction number.")]
    public static Task<string> UndoChange(
        [Description("The transaction to undo: the number shown as [transaction: N] by get_recent_changes.")]
        int transactionId,
        [Description("true only lists what the undo would change and its conflicts, writing nothing (default false).")]
        bool dryRun = false,
        [Description("true undoes despite conflicts, overwriting later changes to these records and possibly leaving links dangling; only after a separate confirmation (default false).")]
        bool force = false,
        GrampsApiClient client = null!,
        McpServer server = null!,
        CancellationToken cancellationToken = default) =>
        UndoChangeAsync(transactionId, dryRun, force, client, PollInterval, WaitLimit,
            UserConfirmation.For(server, "undo_change"), cancellationToken);

    internal static async Task<string> UndoChangeAsync(
        int transactionId, bool dryRun, bool force, GrampsApiClient client, TimeSpan pollInterval, TimeSpan waitLimit,
        IUserConfirmation? confirmation = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (transactionId <= 0)
                throw McpToolErrors.ValidationError(
                    "transactionId must be a transaction number from get_recent_changes ([transaction: N]).");
            if (!dryRun)
                client.EnsureWritable();

            Plan? confirmed = null;
            if (!dryRun && (confirmation ?? UserConfirmation.None).Asks)
            {
                // Asks before taking the update lease: the user may take minutes to answer, and this
                // server's other update tools must not wait for them meanwhile.
                var (early, asked) = await CheckAsync(client, transactionId, dryRun, force);
                if (early is not null)
                    return early;
                var answer = await confirmation!.AskAsync(FormatQuestion(asked!, force), cancellationToken);
                if (answer != ConfirmationAnswer.Confirmed)
                    return Header(transactionId, UserConfirmation.ActionName(answer)) + UserConfirmation.Explain(answer);
                confirmed = asked;
            }

            Plan plan;
            (HttpStatusCode Status, string Body) reply;
            var message = "";
            // Holds off this server's other update tools from the check until Gramps Web has the undo.
            using (dryRun ? null : await client.BeginUpdateAsync())
            {
                var (early, current) = await CheckAsync(client, transactionId, dryRun, force);
                if (early is not null)
                    return early;
                plan = current!;
                if (confirmed is not null && Digest(plan) != Digest(confirmed))
                    return FormatChangedWhileDeciding(plan);

                message = UndoMessage(plan.Transaction);
                var path = $"/api/transactions/history/{transactionId}/undo?message={Uri.EscapeDataString(message)}" +
                           (force ? "&force=true" : "");
                try
                {
                    reply = await client.PostActionAsync(path);
                }
                catch (GrampsApiException ex) when (IsObjectChanged(ex.ResponseBody))
                {
                    return FormatChangedMeanwhile(plan);
                }
            }

            if (reply.Status == HttpStatusCode.Accepted)
            {
                var taskId = ReadTaskId(reply.Body);
                if (taskId is null)
                    return FormatPending(plan, null, message);
                var (state, error) = await WaitForTaskAsync(client, taskId, pollInterval, waitLimit);
                if (state == TaskState.Pending)
                    return FormatPending(plan, taskId, message);
                if (state == TaskState.Failed)
                {
                    if (IsObjectChanged(error))
                        return FormatChangedMeanwhile(plan);
                    throw new InvalidOperationException(
                        $"Gramps Web could not undo transaction {transactionId}: {error ?? "the background task failed"}. Nothing was changed.");
                }
            }

            var undoTransaction = await FindUndoTransactionAsync(client, plan, message);
            return FormatUndone(plan, force, undoTransaction);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private sealed record Change(GrampsTransactionChange Source, string Class, string Collection, string Handle, int Kind);

    /// <summary>Summary labels by handle, and the Gramps IDs of deleted records, read from their stored data.</summary>
    private sealed record Names(IReadOnlyDictionary<string, string> Labels, IReadOnlyDictionary<string, string> GrampsIds)
    {
        public static readonly Names None = new(new Dictionary<string, string>(), new Dictionary<string, string>());
    }

    private sealed record Plan(
        GrampsTransaction Transaction,
        IReadOnlyList<Change> Changes,
        IReadOnlyList<string> Conflicts,
        Names Names,
        bool LinksChecked,
        bool DeletionNeedsForce,
        string? ServerVersion);

    /// <summary>
    /// Reads the transaction and plans its undo. Returns the reply instead when there is nothing to undo,
    /// for a dry run, and when a conflict or an older server stops the undo without force.
    /// </summary>
    private static async Task<(string? Reply, Plan? Plan)> CheckAsync(
        GrampsApiClient client, int transactionId, bool dryRun, bool force)
    {
        var transaction = await client.GetOrNullIfNotFoundAsync<GrampsTransaction>(
            $"/api/transactions/history/{transactionId}?old=true");
        if (transaction is null)
            return ($"Transaction not found: {transactionId}\n\nHint: get_recent_changes lists the latest transactions with their numbers.", null);

        var plan = await PlanAsync(client, transaction, needVersion: !force);
        if (plan.Changes.Count == 0)
            return (Header(transactionId, "unchanged") +
                    $"Transaction {transactionId} ({SystemFormatter.FormatTransactionSummary(transaction)}) " +
                    "changed no records, so there is nothing to undo.", null);
        if (dryRun)
            return (FormatChecked(plan, force), null);
        if (!force && plan.Conflicts.Count > 0)
            return (FormatRefused(plan), null);
        if (!force && plan.DeletionNeedsForce)
            return (FormatOldServerRefusal(plan), null);
        return (null, plan);
    }

    /// <summary>What the user confirmed: the records, what undoing does to each, and the conflicts forced past.</summary>
    private static string Digest(Plan plan) =>
        string.Join("\n", plan.Changes.Select(c => $"{c.Class}|{c.Handle}|{c.Kind}")
            .Concat(plan.Conflicts)
            .Append(plan.DeletionNeedsForce.ToString()));

    /// <summary>
    /// Reads Gramps Web's own conflict check and adds the link checks it lacks: Gramps writes a deleted or
    /// earlier version back as stored, so links into or out of it are not repaired.
    /// </summary>
    private static async Task<Plan> PlanAsync(GrampsApiClient client, GrampsTransaction transaction, bool needVersion)
    {
        var all = (transaction.Changes ?? [])
            .Where(c => c.ObjClass?.Trim() is { Length: > 0 } cls && cls != ReferenceClass
                        && !string.IsNullOrWhiteSpace(c.ObjHandle)
                        && c.TransType is Added or Updated or Deleted
                        && SearchFormatter.CollectionOf(cls) != null)
            .Select(c => new Change(c, c.ObjClass!.Trim(), SearchFormatter.CollectionOf(c.ObjClass.Trim())!,
                c.ObjHandle!.Trim(), c.TransType!.Value))
            .ToList();
        // A record changed more than once, such as a person added and then linked to a family added after it,
        // counts once: as added when the transaction added it, else as its last change, with its version from
        // before the transaction.
        var changes = all.GroupBy(c => (c.Collection, c.Handle))
            .Select(group => group.Count() == 1
                ? group.First()
                : group.First() with { Kind = group.First().Kind == Added ? Added : group.Last().Kind })
            .ToList();
        if (changes.Count == 0)
            return new Plan(transaction, changes, [], Names.None, false, false, null);

        var check = await client.GetAsync<GrampsUndoCheck>($"/api/transactions/history/{transaction.Id}/undo");
        var linksChecked = changes.Count <= MaxCheckedRecords;
        var danglingIn = linksChecked ? await FindLinksIntoRemovedAsync(client, changes) : [];
        var danglingOut = linksChecked ? await FindMissingTargetsAsync(client, changes) : [];

        var hasDeletions = changes.Any(c => c.Kind == Deleted);
        var version = needVersion && hasDeletions ? await ReadServerVersionAsync(client) : null;
        var deletionNeedsForce = version is { } v && ParseVersion(v) is { } parsed && parsed < DeletionUndoFixed;

        var labelHandles = changes.Take(SystemFormatter.MaxChangesShownPerTransaction)
            .Where(c => c.Kind != Deleted)
            .Select(c => (c.Collection, c.Handle))
            .Concat((check.Conflicts ?? []).Select(c => (Collection: SearchFormatter.CollectionOf(c.ObjectClass) ?? "", Handle: c.Handle?.Trim() ?? "")))
            .Concat(danglingIn.SelectMany(d => d.Sources.Take(MaxLinksNamed).Prepend((d.Record.Collection, d.Record.Handle))))
            .Concat(danglingOut.Select(d => (d.Record.Collection, d.Record.Handle)))
            .Where(link => link.Collection.Length > 0 && link.Handle.Length > 0)
            .GroupBy(link => link.Collection)
            .Select(group => (group.Key, (IEnumerable<string>?)group.Select(link => link.Handle).ToList()));
        var labels = new Dictionary<string, string>(await LinkedObjectLabels.LoadAsync(client, labelHandles), StringComparer.Ordinal);
        var grampsIds = new Dictionary<string, string>(StringComparer.Ordinal);
        await AddDeletedLabelsAsync(client, changes, labels, grampsIds);
        var names = new Names(labels, grampsIds);

        var conflicts = new List<string>();
        foreach (var conflict in ChangedLater(check.Conflicts ?? [], all))
            conflicts.Add(FormatServerConflict(conflict, changes, names));
        foreach (var (record, sources) in danglingIn)
        {
            var named = string.Join(", ", sources.Take(MaxLinksNamed).Select(s => Describe(s.Collection, s.Handle, names)));
            var more = sources.Count > MaxLinksNamed ? $" and {sources.Count - MaxLinksNamed} more" : "";
            conflicts.Add($"{Describe(record, names)} is now linked from {named}{more}, which this transaction did not " +
                          "change; deleting it would leave those links dangling.");
        }
        foreach (var (record, targets) in danglingOut)
        {
            var named = string.Join(", ", targets.Take(MaxLinksNamed).Select(t => $"{ClassOf(t.Collection)} [handle: {t.Handle}]"));
            var more = targets.Count > MaxLinksNamed ? $" and {targets.Count - MaxLinksNamed} more" : "";
            conflicts.Add($"The version of {Describe(record, names)} to restore links {named}{more}, which no longer " +
                          "exist; restoring it would leave those links dangling.");
        }

        return new Plan(transaction, changes, conflicts, names, linksChecked, deletionNeedsForce, version);
    }

    /// <summary>
    /// Gramps Web's conflicts, less those of records the transaction changed more than once and nobody changed
    /// later. Its check compares every change with the record as it is now, so all but the last change of such a
    /// record conflict; only when all of them do was the record changed after the transaction. The undo itself
    /// reverts the changes one after another and passes.
    /// </summary>
    private static IEnumerable<GrampsUndoConflict> ChangedLater(IReadOnlyList<GrampsUndoConflict> conflicts,
        IReadOnlyList<Change> changes)
    {
        var counts = changes.CountBy(c => (c.Collection, c.Handle)).ToDictionary();
        return conflicts
            .GroupBy(c => (Collection: SearchFormatter.CollectionOf(c.ObjectClass) ?? "", Handle: c.Handle?.Trim() ?? ""))
            .SelectMany(group =>
            {
                var count = counts.GetValueOrDefault(group.Key);
                if (count <= 1)
                    return group;
                return group.Count() < count ? Enumerable.Empty<GrampsUndoConflict>() : group.Take(1);
            });
    }

    /// <summary>Records the undo deletes that records outside this transaction now link to.</summary>
    private static async Task<List<(Change Record, List<(string Collection, string Handle)> Sources)>> FindLinksIntoRemovedAsync(
        GrampsApiClient client, IReadOnlyList<Change> changes)
    {
        var touched = changes.Select(c => c.Handle).ToHashSet(StringComparer.Ordinal);
        var result = new List<(Change, List<(string, string)>)>();
        foreach (var group in changes.Where(c => c.Kind == Added).GroupBy(c => c.Collection))
        {
            var found = await client.GetByHandlesAsync<BacklinkProbe>(group.Key, group.Select(c => c.Handle),
                probe => probe.Handle, "keys=handle,backlinks&backlinks=true");
            foreach (var change in group)
            {
                if (!found.TryGetValue(change.Handle, out var probe))
                    continue;
                var sources = (probe.Backlinks ?? [])
                    .Where(pair => SearchFormatter.CollectionOf(pair.Key) != null)
                    .SelectMany(pair => (pair.Value ?? [])
                        .Where(h => !string.IsNullOrWhiteSpace(h) && !touched.Contains(h.Trim()))
                        .Select(h => (SearchFormatter.CollectionOf(pair.Key)!, h.Trim())))
                    .Distinct()
                    .ToList();
                if (sources.Count > 0)
                    result.Add((change, sources));
            }
        }

        return result;
    }

    /// <summary>
    /// Records whose version to restore links records that no longer exist; records this undo restores
    /// itself count as existing.
    /// </summary>
    private static async Task<List<(Change Record, List<(string Collection, string Handle)> Targets)>> FindMissingTargetsAsync(
        GrampsApiClient client, IReadOnlyList<Change> changes)
    {
        var restored = changes.Where(c => c.Kind == Deleted).Select(c => c.Handle).ToHashSet(StringComparer.Ordinal);
        var targetsByRecord = changes
            .Where(c => c.Kind is Updated or Deleted && c.Source.OldData is { ValueKind: JsonValueKind.Object })
            .Select(c => (Record: c, Targets: UndoLinkTargets.Collect(c.Source.OldData!.Value)
                .Where(t => !restored.Contains(t.Handle))
                .ToList()))
            .Where(r => r.Targets.Count > 0)
            .ToList();

        var existing = new HashSet<(string, string)>();
        foreach (var group in targetsByRecord.SelectMany(r => r.Targets).Distinct().GroupBy(t => t.Collection))
        {
            var found = await client.GetByHandlesAsync<BacklinkProbe>(group.Key, group.Select(t => t.Handle),
                probe => probe.Handle, "keys=handle");
            foreach (var handle in found.Keys)
                existing.Add((group.Key, handle));
        }

        return targetsByRecord
            .Select(r => (r.Record, Targets: r.Targets.Where(t => !existing.Contains(t)).ToList()))
            .Where(r => r.Targets.Count > 0)
            .ToList();
    }

    /// <summary>Names deleted records from their stored data, which no longer has a record to read.</summary>
    private static async Task AddDeletedLabelsAsync(GrampsApiClient client, IReadOnlyList<Change> changes,
        Dictionary<string, string> labels, Dictionary<string, string> grampsIds)
    {
        var deleted = changes.Take(SystemFormatter.MaxChangesShownPerTransaction)
            .Where(c => c.Kind == Deleted && c.Source.OldData is { ValueKind: JsonValueKind.Object })
            .ToList();
        if (deleted.Count == 0)
            return;

        var tables = await GrampsDefaultTypeLabels.PrefetchForSearchAsync(deleted.Select(c => c.Collection), client);
        foreach (var change in deleted)
        {
            var data = change.Source.OldData!.Value;
            if (ReadModel(change.Collection, data) is { } model
                && SearchFormatter.FormatSummary(model, tables)?.Trim() is { Length: > 0 } summary)
                labels[change.Handle] = summary;
            if (data.TryGetProperty("gramps_id", out var id) && id.ValueKind == JsonValueKind.String
                && id.GetString()?.Trim() is { Length: > 0 } grampsId)
                grampsIds[change.Handle] = grampsId;
        }
    }

    /// <summary>
    /// The stored record as the model its summary line needs. A family is left out: its summary names the
    /// partners from data the stored record does not carry.
    /// </summary>
    private static object? ReadModel(string collection, JsonElement data)
    {
        try
        {
            return collection switch
            {
                "people" => data.Deserialize<GrampsPerson>(GrampsJson.Options),
                "events" => data.Deserialize<GrampsEvent>(GrampsJson.Options),
                "places" => data.Deserialize<GrampsPlace>(GrampsJson.Options),
                "sources" => data.Deserialize<GrampsSource>(GrampsJson.Options),
                "citations" => data.Deserialize<GrampsCitation>(GrampsJson.Options),
                "notes" => data.Deserialize<GrampsNote>(GrampsJson.Options),
                "media" => data.Deserialize<GrampsMedia>(GrampsJson.Options),
                "repositories" => data.Deserialize<GrampsRepository>(GrampsJson.Options),
                "tags" => data.Deserialize<GrampsTag>(GrampsJson.Options),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null; // The record keeps its bare handle.
        }
    }

    private static async Task<string?> ReadServerVersionAsync(GrampsApiClient client)
    {
        try
        {
            var metadata = await client.GetAsync<JsonElement>("/api/metadata/");
            return metadata.TryGetProperty("gramps_webapi", out var api) && api.ValueKind == JsonValueKind.Object
                   && api.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (GrampsApiException)
        {
            return null; // Unknown: the undo itself tells whether it needs force.
        }
    }

    /// <summary>Major and minor of a version such as "3.22.1" or "3.23.0rc1"; null when it has none.</summary>
    internal static Version? ParseVersion(string version)
    {
        var parts = version.Trim().TrimStart('v').Split('.');
        return parts.Length >= 2
               && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
               && int.TryParse(new string(parts[1].TakeWhile(char.IsAsciiDigit).ToArray()), NumberStyles.None,
                   CultureInfo.InvariantCulture, out var minor)
            ? new Version(major, minor)
            : null;
    }

    private enum TaskState { Succeeded, Failed, Pending }

    /// <summary>
    /// Follows a background undo through <c>GET /api/tasks/{id}</c>: <c>state</c> is Celery's, and newer
    /// servers add <c>status</c>, which reports <c>lost</c> for a task whose worker died.
    /// </summary>
    private static async Task<(TaskState State, string? Error)> WaitForTaskAsync(
        GrampsApiClient client, string taskId, TimeSpan pollInterval, TimeSpan waitLimit)
    {
        var deadline = DateTime.UtcNow + waitLimit;
        while (true)
        {
            JsonElement status;
            try
            {
                status = await client.GetAsync<JsonElement>($"/api/tasks/{Uri.EscapeDataString(taskId)}");
            }
            catch (GrampsApiException)
            {
                return (TaskState.Pending, null);
            }

            var state = ReadString(status, "state")?.ToUpperInvariant();
            var lifecycle = ReadString(status, "status")?.ToLowerInvariant();
            if (state == "SUCCESS" || lifecycle == "succeeded")
                return (TaskState.Succeeded, null);
            if (state is "FAILURE" or "REVOKED" || lifecycle is "failed" or "lost")
                return (TaskState.Failed, ReadString(status, "result") ?? ReadString(status, "info")
                    ?? (lifecycle == "lost" ? "the background worker stopped" : null));
            if (DateTime.UtcNow + pollInterval > deadline)
                return (TaskState.Pending, null);
            await Task.Delay(pollInterval);
        }
    }

    private static string? ReadTaskId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("task", out var task)
                ? ReadString(task, "id")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The undo's own transaction: the newest after the undone one that carries its message, or on servers
    /// that ignore the message, the same records.
    /// </summary>
    private static async Task<int?> FindUndoTransactionAsync(GrampsApiClient client, Plan plan, string message)
    {
        try
        {
            var latest = await client.GetPagedListAsync<GrampsTransaction>(
                "/api/transactions/history/?page=1&pagesize=10&sort=-id");
            var records = plan.Changes.Select(c => (c.Class, c.Handle)).ToHashSet();
            return (latest.Objects ?? [])
                .Where(t => t.Id > plan.Transaction.Id)
                .FirstOrDefault(t => t.Description?.Trim() == message
                                     || (t.Changes ?? [])
                                         .Where(c => c.ObjClass?.Trim() != ReferenceClass)
                                         .Select(c => (c.ObjClass?.Trim() ?? "", c.ObjHandle?.Trim() ?? ""))
                                         .ToHashSet().SetEquals(records))
                ?.Id;
        }
        catch (GrampsApiException)
        {
            return null;
        }
    }

    private static string UndoMessage(GrampsTransaction transaction)
    {
        var description = transaction.Description?.Trim();
        if (string.IsNullOrEmpty(description))
            return $"Undo transaction {transaction.Id}";
        if (description.Length > MaxDescriptionInMessage)
            description = description[..(MaxDescriptionInMessage - 1)].TrimEnd() + "…";
        return $"Undo transaction {transaction.Id}: {description}";
    }

    private static bool IsObjectChanged(string? text) =>
        text?.Contains("Object has changed", StringComparison.OrdinalIgnoreCase) == true;

    private static string? ReadString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FormatServerConflict(GrampsUndoConflict conflict, IReadOnlyList<Change> changes, Names names)
    {
        var handle = conflict.Handle?.Trim() ?? "";
        var record = Describe(SearchFormatter.CollectionOf(conflict.ObjectClass) ?? "", handle, names, conflict.ObjectClass);
        var kind = changes.FirstOrDefault(c => c.Handle == handle)?.Kind;
        return conflict.ConflictType switch
        {
            "object_changed" when kind == Added => $"{record} was changed or deleted after this transaction added it.",
            "object_changed" => $"{record} was changed or deleted after this transaction.",
            "object_exists" => $"{record} exists again, so the record this transaction deleted cannot be restored.",
            _ => $"{record}: {conflict.Description?.Trim() ?? "could not be checked"}."
        };
    }

    private static string Describe(Change change, Names names) =>
        Describe(change.Collection, change.Handle, names, change.Class);

    /// <summary>
    /// "Note N0012: [General] Text [handle: h]"; the Gramps ID only for a deleted record. <paramref name="forUser"/>
    /// leaves out the handle when the ID or the label names the record.
    /// </summary>
    private static string Describe(string collection, string handle, Names names, string? className = null,
        bool forUser = false)
    {
        var sb = new StringBuilder(className?.Trim() is { Length: > 0 } cls ? cls : ClassOf(collection));
        var named = false;
        if (names.GrampsIds.TryGetValue(handle, out var grampsId))
        {
            sb.Append(' ').Append(grampsId);
            named = true;
        }
        if (names.Labels.TryGetValue(handle, out var label))
        {
            sb.Append(": ").Append(label);
            named = true;
        }
        return forUser && named ? sb.ToString() : sb.Append($" [handle: {handle}]").ToString();
    }

    private static string ClassOf(string collection) => collection switch
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
        _ => "Object"
    };

    private static string Header(int transactionId, string action, int? conflicts = null,
        int? undoTransaction = null, string? task = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Transaction");
        sb.AppendLine($"action: {action}");
        sb.AppendLine($"transaction: {transactionId}");
        if (undoTransaction is { } undo)
            sb.AppendLine($"undo_transaction: {undo}");
        if (task is not null)
            sb.AppendLine($"task: {task}");
        if (conflicts is { } count)
            sb.AppendLine($"conflicts: {count}");
        sb.AppendLine("---");
        return sb.ToString();
    }

    /// <summary>One line per record, verbs by what undoing its change does.</summary>
    private static void AppendChanges(StringBuilder sb, Plan plan, bool done)
    {
        foreach (var change in plan.Changes.Take(SystemFormatter.MaxChangesShownPerTransaction))
        {
            var verb = (change.Kind, done) switch
            {
                (Added, true) => "Deleted",
                (Added, false) => "Delete",
                (Updated, true) => "Restored the earlier version of",
                (Updated, false) => "Restore the earlier version of",
                (_, true) => "Restored the deleted",
                _ => "Restore the deleted"
            };
            sb.AppendLine($"   {verb} {Describe(change, plan.Names)}");
        }

        if (plan.Changes.Count > SystemFormatter.MaxChangesShownPerTransaction)
            sb.AppendLine($"   … (+{plan.Changes.Count - SystemFormatter.MaxChangesShownPerTransaction} more records)");
    }

    private static void AppendConflicts(StringBuilder sb, Plan plan, string title)
    {
        if (plan.Conflicts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{title} ({plan.Conflicts.Count}):");
            foreach (var conflict in plan.Conflicts)
                sb.AppendLine($"  • {conflict}");
        }

        if (!plan.LinksChecked)
        {
            sb.AppendLine();
            sb.AppendLine($"Links were not checked for a transaction of more than {MaxCheckedRecords} records; " +
                          "Gramps Web checked only for later changes to them.");
        }
    }

    private static string Summary(Plan plan) =>
        $"transaction {plan.Transaction.Id} ({SystemFormatter.FormatTransactionSummary(plan.Transaction)})";

    private static string FormatChecked(Plan plan, bool force)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "checked", plan.Conflicts.Count));
        sb.AppendLine($"Nothing was changed. Undoing {Summary(plan)} would:");
        AppendChanges(sb, plan, done: false);
        AppendConflicts(sb, plan, "Conflicts");
        sb.AppendLine();
        if (plan.Conflicts.Count > 0)
            sb.AppendLine(force
                ? "With force=true the undo would overwrite those later changes and leave such links dangling."
                : ConflictAdvice);
        else if (plan.DeletionNeedsForce)
            sb.AppendLine(OldServerExplanation(plan));
        else
            sb.AppendLine("No conflicts: after the user confirms, undo_change without dryRun applies it.");
        return sb.ToString();
    }

    private const string ConflictAdvice =
        "Undo the later transactions that touched these records first, newest first (get_recent_changes lists them), " +
        "or pass force=true only after the user separately confirms overwriting those changes and leaving such links dangling.";

    private static string FormatRefused(Plan plan)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "refused", plan.Conflicts.Count));
        sb.AppendLine($"Nothing was changed: undoing {Summary(plan)} would overwrite or break later changes. It would:");
        AppendChanges(sb, plan, done: false);
        AppendConflicts(sb, plan, "Conflicts");
        sb.AppendLine();
        sb.AppendLine(ConflictAdvice);
        return sb.ToString();
    }

    private static string OldServerExplanation(Plan plan) =>
        $"Gramps Web {plan.ServerVersion} restores deleted records only with force=true (fixed in 3.23). No later " +
        "change conflicts with this undo, so force=true would apply exactly the changes above; pass it only after " +
        "the user confirms, or update Gramps Web first.";

    private static string FormatOldServerRefusal(Plan plan)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "refused", 0));
        sb.AppendLine($"Nothing was changed. Undoing {Summary(plan)} would:");
        AppendChanges(sb, plan, done: false);
        sb.AppendLine();
        sb.AppendLine(OldServerExplanation(plan));
        return sb.ToString();
    }

    private static string FormatChangedMeanwhile(Plan plan)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "refused"));
        sb.AppendLine($"Nothing was changed: Gramps Web refused to undo {Summary(plan)} because a record it would " +
                      "write no longer matches this transaction.");
        if (plan.Changes.Any(c => c.Kind == Deleted))
            sb.AppendLine("Gramps Web before 3.23 refuses this for every transaction that deleted records; with an " +
                          "older server, pass force=true only after the user confirms.");
        sb.AppendLine("Otherwise a record changed after the check: run undo_change with dryRun to see the conflicts.");
        return sb.ToString();
    }

    private static string FormatChangedWhileDeciding(Plan plan)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "refused", plan.Conflicts.Count));
        sb.AppendLine($"Nothing was changed: the records of {Summary(plan)} or their conflicts changed while the user " +
                      "was deciding. Check again with dryRun and ask the user before calling again.");
        return sb.ToString();
    }

    /// <summary>
    /// The confirmation question in plain text, as clients show it: what undoing does to each record,
    /// and with force the conflicts it overrides. Records are named without handles.
    /// </summary>
    private static string FormatQuestion(Plan plan, bool force)
    {
        var sb = new StringBuilder($"Undo {Summary(plan)}?");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("It will:");
        foreach (var change in plan.Changes.Take(SystemFormatter.MaxChangesShownPerTransaction))
        {
            var verb = change.Kind switch
            {
                Added => "Delete",
                Updated => "Restore the earlier version of",
                _ => "Restore the deleted"
            };
            sb.AppendLine($"  • {verb} {Describe(change.Collection, change.Handle, plan.Names, change.Class, forUser: true)}");
        }

        if (plan.Changes.Count > SystemFormatter.MaxChangesShownPerTransaction)
            sb.AppendLine($"  • … and {plan.Changes.Count - SystemFormatter.MaxChangesShownPerTransaction} more records");

        if (force && plan.Conflicts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Conflicts it will override ({plan.Conflicts.Count}):");
            foreach (var conflict in plan.Conflicts)
                sb.AppendLine($"  • {WithoutHandles(conflict)}");
            sb.AppendLine("Overriding overwrites later changes to these records and may leave links to missing records.");
        }

        sb.AppendLine();
        sb.Append("The undo is a new transaction, which undo_change can revert in turn.");
        return sb.ToString();
    }

    /// <summary>Drops the " [handle: …]" of the records a conflict names.</summary>
    private static string WithoutHandles(string text) => HandleTag().Replace(text, "");

    [GeneratedRegex(@" \[handle: [^\]]+\]")]
    private static partial Regex HandleTag();

    private static string FormatPending(Plan plan, string? taskId, string message)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "pending", task: taskId));
        sb.AppendLine($"Gramps Web is still undoing {Summary(plan)} in the background. Do not call undo_change for it " +
                      $"again: get_recent_changes shows a transaction \"{message}\" once it is done.");
        return sb.ToString();
    }

    private static string FormatUndone(Plan plan, bool force, int? undoTransaction)
    {
        var sb = new StringBuilder(Header(plan.Transaction.Id, "undone", undoTransaction: undoTransaction));
        sb.AppendLine($"Undid {Summary(plan)}:");
        AppendChanges(sb, plan, done: true);
        if (force)
            AppendConflicts(sb, plan, "Forced past conflicts");
        sb.AppendLine();
        sb.AppendLine(undoTransaction is { } undo
            ? $"This undo is transaction {undo}; undo_change(transactionId: {undo}) reverts it."
            : "get_recent_changes lists this undo as a new transaction, which undo_change can revert.");
        return sb.ToString();
    }

    /// <summary>The handle and backlinks of a record (<c>keys=handle,backlinks&amp;backlinks=true</c>).</summary>
    private sealed class BacklinkProbe
    {
        [JsonPropertyName("handle")]
        public string? Handle { get; set; }

        /// <summary>Handles by lower-case class, such as <c>"person": [handles]</c>.</summary>
        [JsonPropertyName("backlinks")]
        public Dictionary<string, string[]?>? Backlinks { get; set; }
    }
}
