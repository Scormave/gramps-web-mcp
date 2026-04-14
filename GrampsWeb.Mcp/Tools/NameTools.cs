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
        "Read-only: name display format definitions and surname grouping rules configured in this tree.")]
    public static async Task<string> GetNameSettings(GrampsApiClient client)
    {
        try
        {
            var formats = await client.GetAsync<dynamic>("/api/name-formats/");
            var groups = await client.GetAsync<dynamic>("/api/name-groups/");
            return $"NAME FORMATS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(formats)}\n\n" +
                   $"NAME GROUPS\n{new string('=', 60)}\n\n{JsonResponseFormatter.FormatDynamic(groups)}";
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

}
