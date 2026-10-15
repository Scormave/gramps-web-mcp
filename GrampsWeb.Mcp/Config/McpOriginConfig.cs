namespace GrampsWeb.Mcp.Config;

/// <summary>
/// Browser origins allowed to call the MCP HTTP/SSE endpoints. Browsers send an <c>Origin</c> header with the
/// requests that carry MCP messages; MCP clients outside a browser send none and are not affected.
/// </summary>
public sealed record McpOriginConfig(IReadOnlySet<string> AllowedOrigins)
{
    /// <summary>
    /// <list type="bullet">
    /// <item><description><c>MCP_ALLOWED_ORIGINS</c> — optional comma-separated browser origins for HTTP/SSE transport, such as <c>https://app.example.com</c>; every other origin is refused</description></item>
    /// </list>
    /// </summary>
    public static McpOriginConfig FromEnvironment(McpListenMode mode) =>
        Create(Environment.GetEnvironmentVariable("MCP_ALLOWED_ORIGINS"), mode);

    internal static McpOriginConfig Create(string? rawOrigins, McpListenMode mode)
    {
        var origins = new HashSet<string>(StringComparer.Ordinal);
        if (mode == McpListenMode.Stdio || string.IsNullOrWhiteSpace(rawOrigins))
        {
            return new McpOriginConfig(origins);
        }

        var invalid = new List<string>();
        foreach (var entry in rawOrigins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryNormalize(entry, out var origin))
            {
                origins.Add(origin);
            }
            else
            {
                invalid.Add(entry);
            }
        }

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuration validation failed:\n" +
                $"  • MCP_ALLOWED_ORIGINS entries must be origins such as https://app.example.com (http or https, a host and an optional port, no path): {string.Join(", ", invalid)}");
        }

        return new McpOriginConfig(origins);
    }

    /// <summary>True when an <c>Origin</c> header value names an allowed origin.</summary>
    public bool IsAllowed(string origin) =>
        TryNormalize(origin, out var normalized) && AllowedOrigins.Contains(normalized);

    /// <summary>
    /// Reduces an origin to scheme, host and port, so that letter case, a default port, a trailing slash and a
    /// non-ASCII host spelling compare equal to the ASCII form browsers send. Accepts nothing but http and https
    /// origins: <c>null</c>, paths, queries and user names are refused.
    /// </summary>
    internal static bool TryNormalize(string value, out string origin)
    {
        origin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            return false;
        }

        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        origin = $"{uri.Scheme}://{host}:{uri.Port}";
        return true;
    }
}
