using System.ComponentModel;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
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
        "Read-only: recent transaction history (most recently changed objects). " +
        "Use for sync auditing or 'what changed last' workflows.")]
    public static async Task<string> GetRecentChanges(
        [Description("How many history rows (clamped 1–100). Default 20.")]
        int limit = 20,
        GrampsApiClient client = null!)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 100);
            // Transaction history is read via /transactions/history; /transactions only accepts POST.
            var changes = await client.GetAsync<JsonElement>($"/api/transactions/history/?pagesize={limit}&sort=-id");
            return SystemFormatter.FormatRecentChanges(changes);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool(Title = "Get Bookmarks", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: Gramps Web user bookmarks (saved shortcuts to people, families, etc.).")]
    public static async Task<string> GetBookmarks(GrampsApiClient client)
    {
        try
        {
            var bookmarks = await client.GetAsync<JsonElement>("/api/bookmarks/");
            return SystemFormatter.FormatBookmarks(bookmarks);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
