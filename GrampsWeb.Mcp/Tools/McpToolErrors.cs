using GrampsWeb.Mcp.Exceptions;
using ModelContextProtocol;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Maps failures to <see cref="McpException"/> so the MCP runtime sets <c>isError</c> on tool results
/// (string returns are always treated as success).
/// </summary>
internal static class McpToolErrors
{
    /// <summary>
    /// Use as <c>throw ToMcpException(ex);</c> in catch blocks (a plain call to a no-return helper is not
    /// treated as terminating the catch for flow analysis).
    /// </summary>
    public static Exception ToMcpException(Exception ex)
    {
        if (ex is McpException m)
            throw m;
        if (ex is GrampsApiException g)
            return new McpException(g.Message, g);
        return new McpException(ex.Message, ex);
    }

    /// <summary>Invalid arguments / client validation — use <c>throw ValidationError(message);</c>.</summary>
    public static Exception ValidationError(string message) =>
        new McpException(message);
}
