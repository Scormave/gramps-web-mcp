namespace GrampsWeb.Mcp.Config;

/// <summary>
/// How the MCP server listens: stdio (default), Streamable HTTP, or legacy SSE endpoints.
/// </summary>
public enum McpListenMode
{
    /// <summary>stdin/stdout JSON-RPC for local clients (e.g. Claude Desktop).</summary>
    Stdio,
    /// <summary>Streamable HTTP at <see cref="MapPath"/> (recommended remote transport; responses stream as SSE).</summary>
    Http,
    /// <summary>Legacy MCP HTTP+SSE: long-lived GET <c>{MapPath}/sse</c> and POST <c>{MapPath}/message</c>. Stateful only.</summary>
    Sse
}

/// <summary>
/// MCP wire transport settings from environment variables.
/// </summary>
public sealed record McpTransportConfig(
    McpListenMode Mode,
    string MapPath,
    bool Stateless,
    bool EnableLegacySse)
{
    /// <summary>
    /// <list type="bullet">
    /// <item><description><c>MCP_TRANSPORT</c> — <c>stdio</c> (default), <c>http</c>, or <c>sse</c></description></item>
    /// <item><description><c>ASPNETCORE_URLS</c> — listen URLs for HTTP/SSE (e.g. <c>http://127.0.0.1:8080</c>)</description></item>
    /// <item><description><c>MCP_PATH</c> — URL prefix for MCP (default <c>/mcp</c>). Legacy SSE lives at <c>{path}/sse</c>.</description></item>
    /// <item><description><c>MCP_STATELESS</c> — <c>true</c>/<c>false</c> for Streamable HTTP (default <c>true</c> when <c>MCP_TRANSPORT=http</c>)</description></item>
    /// <item><description><c>MCP_ENABLE_LEGACY_SSE</c> — with <c>http</c>, also map legacy <c>/sse</c> and <c>/message</c> (forces stateful)</description></item>
    /// </list>
    /// </summary>
    public static McpTransportConfig FromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_TRANSPORT")?.Trim().ToLowerInvariant();
        var mode = raw switch
        {
            "http" => McpListenMode.Http,
            "sse" => McpListenMode.Sse,
            _ => McpListenMode.Stdio
        };

        var path = Environment.GetEnvironmentVariable("MCP_PATH")?.Trim() ?? "/mcp";
        if (path.Length > 0 && !path.StartsWith('/'))
            path = "/" + path;

        var stateless = ParseBoolOrDefault(Environment.GetEnvironmentVariable("MCP_STATELESS"), defaultValue: true);
        var legacyFromEnv = ParseBoolOrDefault(Environment.GetEnvironmentVariable("MCP_ENABLE_LEGACY_SSE"), defaultValue: false);

        if (mode == McpListenMode.Sse)
        {
            return new McpTransportConfig(mode, path, Stateless: false, EnableLegacySse: true);
        }

        if (mode == McpListenMode.Http && legacyFromEnv)
        {
            return new McpTransportConfig(mode, path, Stateless: false, EnableLegacySse: true);
        }

        return new McpTransportConfig(mode, path, stateless, EnableLegacySse: false);
    }

    private static bool ParseBoolOrDefault(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }
}
