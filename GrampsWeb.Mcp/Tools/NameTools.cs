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
        "Read-only: name display format definitions configured in this tree (how Gramps renders names in the UI). " +
        "Distinct from get_name_schema (person name JSON for create/update).")]
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
        "Read-only: surname grouping rules (e.g. Smith vs Smythe) for this database.")]
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
