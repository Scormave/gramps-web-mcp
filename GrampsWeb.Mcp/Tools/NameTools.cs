using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tools for reading name-related metadata and preferences.
/// </summary>
[McpServerToolType]
public static class NameTools
{
    [McpServerTool]
    [Description(
        "Get all configured name format options for this database. " +
        "Returns the list of name format definitions (how names are displayed).")]
    public static async Task<string> GetNameFormats(GrampsApiClient client)
    {
        try
        {
            var result = await client.GetAsync<dynamic>("/api/name-formats/");
            return $"NAME FORMATS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(result)}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "Get all configured name grouping rules for this database. " +
        "These rules define surname grouping across name variants (e.g. 'Smith' and 'Smythe').")]
    public static async Task<string> GetNameGroups(GrampsApiClient client)
    {
        try
        {
            var result = await client.GetAsync<dynamic>("/api/name-groups/");
            return $"NAME GROUPS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(result)}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
