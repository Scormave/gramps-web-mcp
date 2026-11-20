using System.ComponentModel;
using System.Net;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading system-level information and database metadata.
/// </summary>
[McpServerToolType]
public static class SystemTools
{
    [McpServerTool(Title = "Get Recent Changes", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Show the tree's recent edit history, newest first: each transaction's number, commit time (UTC), description, " +
        "user, an undo marker, and each changed object's class, change kind (added/updated/deleted) and handle, " +
        "with the first 10 objects of each transaction and how many more it changed. A link added or removed between " +
        "objects appears as Reference from one handle to the other. limit counts transactions, not objects, and stops at " +
        "the latest 100, so older history is out of reach here; list_objects with sort -change lists records by last edit " +
        "instead. With identifier it shows one record's own history instead, from its first save, newest first: per change " +
        "the time, added/updated/deleted, the user and the transaction, and the fields it changed as field: before → after " +
        "(up to 12 per change), with linked records named; this needs Gramps Web API 3.22 or later. Use it to see what " +
        "was changed last and by whom, who changed a field of a record and what it held before, to check that your own " +
        "writes landed, or to find the number undo_change takes to revert a transaction; " +
        "for the dated events of one person use get_timeline, and read a changed record with get_object.")]
    public static async Task<string> GetRecentChanges(
        [Description("How many transactions to show, 1–100 (default 20); with identifier, how many changes of that record. Other values are clamped.")]
        int limit = 20,
        [Description("Show this record's history instead: a Gramps ID (e.g. I0001) or handle. A deleted record is found by its handle, which the transaction that deleted it shows.")]
        string? identifier = null,
        [Description("The record's type, needed with a handle or a Gramps ID in a custom format: person, family, event, place, source, citation, note, media, repository or tag. Omit for a Gramps ID such as I0001; used only with identifier.")]
        string? objectType = null,
        GrampsApiClient client = null!)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 100);
            if (!string.IsNullOrWhiteSpace(identifier))
                return await GetRecordHistoryAsync(identifier.Trim(), objectType, limit, client);
            if (!string.IsNullOrWhiteSpace(objectType))
                throw McpToolErrors.ValidationError(
                    "objectType is used only with identifier: pass the record whose history to show, or omit objectType for the whole tree's history.");

            // Transaction history is read via /transactions/history; /transactions only accepts POST.
            // Without page the endpoint ignores pagesize and returns the entire history.
            var history = await client.GetPagedListAsync<GrampsTransaction>(
                $"/api/transactions/history/?page=1&pagesize={limit}&sort=-id");
            return SystemFormatter.FormatRecentChanges(history.Objects ?? [], history.Total);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>
    /// The changes of one record, newest first, with the fields each changed. Gramps Web keeps a deleted record's
    /// history, so a handle that no longer reads still has one; a Gramps ID must still resolve.
    /// </summary>
    private static async Task<string> GetRecordHistoryAsync(
        string identifier, string? objectType, int limit, GrampsApiClient client)
    {
        var type = ObjectTools.ResolveObjectType(identifier, objectType);
        var (className, collection) = ObjectTools.KindOf(type);
        var handle = await HandleResolver.ResolveToHandleAsync(identifier, client, collection);
        // A value that may be a Gramps ID and did not resolve; a handle-shaped one may be a deleted record's.
        if (type != "tag" && HandleResolver.MayBeGrampsId(identifier) && handle == identifier)
        {
            return NotFoundHelper.NotFoundMessage(className, identifier) +
                   "\n\nA deleted record no longer has its Gramps ID but keeps its history under its handle: " +
                   "get_recent_changes without identifier shows the transaction that deleted it, with the handle.";
        }

        GrampsPagedResult<GrampsObjectChange> history;
        try
        {
            // sort=-id orders by change time; old/new add the record as stored before and after each change.
            history = await client.GetPagedListAsync<GrampsObjectChange>(
                $"/api/transactions/history/objects/{className}/{Uri.EscapeDataString(handle)}" +
                $"?page=1&pagesize={limit}&sort=-id&old=true&new=true");
        }
        catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw McpToolErrors.ValidationError(
                "The history of one record needs Gramps Web API 3.22 or later, and this server does not have it. " +
                "get_recent_changes without identifier lists the tree's latest transactions with the records they changed.");
        }

        var changes = history.Objects ?? [];
        var diffs = changes
            .Select(change => RecordHistoryFormatter.Compare(change.OldData, change.NewData))
            .ToList();
        var linked = RecordHistoryFormatter.LinkedRecords(diffs.SelectMany(d => d))
            .GroupBy(link => link.Collection, StringComparer.Ordinal)
            .Select(group => (group.Key, (IEnumerable<string>?)group.Select(link => link.Handle).ToList()))
            .Append((collection, [handle]));
        var categories = RecordHistoryFormatter.TypeCategoriesOf(diffs.SelectMany(d => d)).ToList();

        var labelsTask = LinkedObjectLabels.LoadAsync(client, linked);
        var typeLabelsTask = Task.WhenAll(categories.Select(async category =>
            (Category: category, Labels: await GrampsDefaultTypeLabels.FetchLabelsAsync(client, category))));
        await Task.WhenAll(labelsTask, typeLabelsTask);
        var labels = await labelsTask;
        var typeLabels = (await typeLabelsTask).ToDictionary(t => t.Category, t => t.Labels, StringComparer.Ordinal);

        var record = await RecordLineAsync(identifier, className, collection, handle, changes, labels, client);
        return RecordHistoryFormatter.FormatHistory(record, changes, diffs, history.Total, labels, typeLabels);
    }

    /// <summary>
    /// "Person I0004: Smith, John [handle: h]": the Gramps ID from the latest stored version, and the label of the
    /// record as it reads now or, once deleted, as it was last stored.
    /// </summary>
    private static async Task<string> RecordLineAsync(string identifier, string className, string collection, string handle,
        IReadOnlyList<GrampsObjectChange> changes, IReadOnlyDictionary<string, string> labels, GrampsApiClient client)
    {
        var latest = changes
            .SelectMany(change => new[] { change.NewData, change.OldData })
            .FirstOrDefault(data => data is { ValueKind: JsonValueKind.Object } d && d.EnumerateObject().Any());
        var grampsId = latest is { } stored && stored.TryGetProperty("gramps_id", out var id)
                                            && id.ValueKind == JsonValueKind.String
            ? id.GetString()?.Trim()
            : handle != identifier ? identifier : null;

        var label = labels.GetValueOrDefault(handle);
        if (label is null && latest is { } data && UndoTools.ReadModel(collection, data) is { } model)
        {
            var tables = await GrampsDefaultTypeLabels.PrefetchForSearchAsync([collection], client);
            label = SearchFormatter.FormatSummary(model, tables)?.Trim();
        }

        var name = string.IsNullOrEmpty(grampsId) ? className : $"{className} {grampsId}";
        return string.IsNullOrEmpty(label) ? $"{name} [handle: {handle}]" : $"{name}: {label} [handle: {handle}]";
    }

    [McpServerTool(Title = "Get Bookmarks", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description(
        "List the records bookmarked in this tree, grouped by type (people, families, events, places, " +
        "sources, citations, repositories, media, notes), each as a one-line summary with its handle. " +
        "Bookmarks belong to the tree and are shared by all its users; they are set in Gramps Web or Gramps desktop, " +
        "not through this server, and a tree may have none. All bookmarks come in one response, without paging: types in the " +
        "order above, records in the tree's bookmark order, and a record that can no longer be read as its bare handle. " +
        "Use it to start from the records the tree's users marked; " +
        "to find records by name use search, to browse one type use list_objects, for recent edits use get_recent_changes.")]
    public static async Task<string> GetBookmarks(GrampsApiClient client)
    {
        try
        {
            // The bookmarked handles by collection: {"people": [handles], "families": [], …}.
            var bookmarks = await client.GetAsync<Dictionary<string, string[]?>>("/api/bookmarks/");
            var labels = await LinkedObjectLabels.LoadAsync(client,
                bookmarks.Select(pair => (pair.Key, (IEnumerable<string>?)pair.Value)));
            return SystemFormatter.FormatBookmarks(bookmarks, labels);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
