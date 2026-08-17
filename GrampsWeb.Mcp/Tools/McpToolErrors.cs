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

    /// <summary>
    /// Same as <see cref="ToMcpException(Exception)"/>, and when <paramref name="createdObjects"/>
    /// is non-empty prepends those IDs so the agent does not retry a whole composite tool.
    /// </summary>
    public static Exception ToMcpException(Exception ex, IReadOnlyCollection<string> createdObjects)
    {
        if (createdObjects.Count == 0)
            return ToMcpException(ex);

        Exception mapped = ex is McpException m
            ? m
            : ex is GrampsApiException g
                ? new McpException(g.Message, g)
                : new McpException(ex.Message, ex);

        var list = string.Join("\n", createdObjects.Select(o => "  • " + o));
        var message =
            "This composite call created some objects before failing. " +
            "Do not retry the whole tool; inspect these objects first and continue from the remaining step.\n" +
            list + "\n\n" + mapped.Message;
        return new McpException(message, mapped);
    }

    /// <summary>Invalid arguments / client validation — use <c>throw ValidationError(message);</c>.</summary>
    public static Exception ValidationError(string message) =>
        new McpException(message);
}
