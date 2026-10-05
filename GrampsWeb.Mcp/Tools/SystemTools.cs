using System.ComponentModel;
using GrampsWeb.Mcp.Client;
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
    [McpServerTool(Title = "Get Recent Changes", ReadOnly = true, Destructive = false)]
    [Description(
        "Show the tree's recent edit history, newest first: commit time (UTC), description, user, " +
        "an undo marker, and each changed object's class, change kind (added/updated/deleted) and handle, " +
        "up to 10 objects per transaction. A link added or removed between objects appears as Reference from one handle to the other. " +
        "Use it to see what was changed last and by whom, or to check that your own writes landed; " +
        "for the dated events of one person use get_timeline, and read a changed record with get_object.")]
    public static async Task<string> GetRecentChanges(
        [Description("How many transactions to show, 1–100 (default 20); other values are clamped.")]
        int limit = 20,
        GrampsApiClient client = null!)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 100);
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

    [McpServerTool(Title = "Get Bookmarks", ReadOnly = true, Destructive = false)]
    [Description(
        "List the records bookmarked in this tree, grouped by type (people, families, events, places, " +
        "sources, citations, repositories, media, notes), each as a one-line summary with its handle. " +
        "Bookmarks belong to the tree and are shared by all its users; they are set in Gramps Web or Gramps desktop, " +
        "not through this server, and a tree may have none. Use it to start from the records the tree's users marked; " +
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
