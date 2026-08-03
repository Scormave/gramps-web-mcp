namespace GrampsWeb.Mcp.Config;

/// <summary>
/// MCP HTTP/SSE authentication settings from environment variables.
/// </summary>
public sealed record McpAuthConfig(
    string[] ApiKeys,
    bool WarnAboutAnonymousAccess)
{
    public const int MinKeyLength = 16;

    /// <summary>
    /// True when at least one API key is configured and the auth gate is active.
    /// </summary>
    public bool Enabled => ApiKeys.Length > 0;

    /// <summary>
    /// <list type="bullet">
    /// <item><description><c>MCP_API_KEY</c> — optional shared secret for HTTP/SSE transport; comma-separated list for rotation</description></item>
    /// </list>
    /// Loopback-only bind detection uses <c>ASPNETCORE_URLS</c> to decide whether to warn about anonymous access.
    /// </summary>
    public static McpAuthConfig FromEnvironment(McpListenMode mode) =>
        Create(
            Environment.GetEnvironmentVariable("MCP_API_KEY"),
            Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
            mode);

    internal static McpAuthConfig Create(string? rawKeys, string? rawUrls, McpListenMode mode)
    {
        if (mode == McpListenMode.Stdio)
        {
            return new McpAuthConfig([], WarnAboutAnonymousAccess: false);
        }

        var keys = ParseApiKeys(rawKeys);
        var errors = new List<string>();

        foreach (var key in keys)
        {
            if (key.Length < MinKeyLength)
            {
                errors.Add($"MCP_API_KEY entries must be at least {MinKeyLength} characters");
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawKeys) && keys.Length == 0)
        {
            errors.Add("MCP_API_KEY is set but contains no valid keys");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuration validation failed:\n" +
                string.Join("\n", errors.Select(e => "  • " + e)));
        }

        var warnAboutAnonymousAccess = keys.Length == 0 && !IsLoopbackOnly(rawUrls);
        return new McpAuthConfig(keys, warnAboutAnonymousAccess);
    }

    internal static bool IsLoopbackOnly(string? rawUrls)
    {
        if (string.IsNullOrWhiteSpace(rawUrls))
        {
            return false;
        }

        var entries = rawUrls.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!IsLoopbackHost(uri.Host))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] ParseApiKeys(string? rawKeys)
    {
        if (string.IsNullOrWhiteSpace(rawKeys))
        {
            return [];
        }

        return rawKeys
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (host.Equals("127.0.0.1", StringComparison.Ordinal))
        {
            return true;
        }

        if (host.Equals("::1", StringComparison.Ordinal) || host.Equals("[::1]", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
