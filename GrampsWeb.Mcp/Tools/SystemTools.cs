using System.ComponentModel;
using System.Text.Json;
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
    [McpServerTool]
    [Description(
        "Get database metadata: API version, tree ID, tree name, owner, and other system information. " +
        "Useful for understanding which database and version you're connected to.")]
    public static async Task<string> GetMetadata(GrampsApiClient client)
    {
        try
        {
            var metadata = await client.GetAsync<JsonElement>("/api/metadata/");
            string? defaultPersonFullName = null;
            if (metadata.TryGetProperty("default_person", out var defaultPersonEl)
                && defaultPersonEl.ValueKind == JsonValueKind.String)
            {
                var handle = defaultPersonEl.GetString();
                if (!string.IsNullOrEmpty(handle))
                {
                    try
                    {
                        var person = await client.GetAsync<GrampsPerson>(
                            $"/api/people/{Uri.EscapeDataString(handle)}");
                        if (person.PrimaryName != null)
                            defaultPersonFullName = GrampsValueFormatter.FormatName(person.PrimaryName);
                    }
                    catch
                    {
                        // Keep handle-only output if the person cannot be loaded.
                    }
                }
            }

            return SystemFormatter.FormatMetadata(metadata, defaultPersonFullName);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get recently modified objects. Returns a list of the most recently changed genealogical objects. " +
        "Useful for seeing what changed recently and tracking database updates.")]
    public static async Task<string> GetRecentChanges(
        [Description("Maximum number of recent changes to return (default: 20, max: 100)")]
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

    [McpServerTool]
    [Description(
        "Get user bookmarks: quick references to important objects in the tree. " +
        "Bookmarks are links that users have saved for quick access to frequently-viewed people or families.")]
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
