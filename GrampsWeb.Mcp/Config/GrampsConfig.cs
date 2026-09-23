namespace GrampsWeb.Mcp.Config;

/// <summary>
/// Configuration for Gramps Web API connection.
/// Loaded from environment variables.
/// </summary>
public record GrampsConfig(
    string ApiUrl,
    string Username,
    string Password,
    string TreeId,
    bool ReadOnly = false,
    bool MediaResourcesEnabled = false,
    long MediaMaxBytes = GrampsConfig.DefaultMediaMaxBytes,
    string[]? MediaAllowedMimeTypes = null,
    bool MediaAllowPrivate = false,
    bool MutationSerialize = true,
    int MutationMinIntervalMs = 0,
    string? RefreshToken = null)
{
    public const long DefaultMediaMaxBytes = 5 * 1024 * 1024;

    public static readonly string[] DefaultMediaAllowedMimeTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/avif",
        "application/pdf"
    ];

    /// <summary>
    /// True when the server authenticates with a long-lived refresh token instead of
    /// a username and password, for Gramps Web instances with local login disabled.
    /// </summary>
    public bool UsesRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    public string[] EffectiveMediaAllowedMimeTypes =>
        MediaAllowedMimeTypes is { Length: > 0 }
            ? MediaAllowedMimeTypes
            : DefaultMediaAllowedMimeTypes;

    /// <summary>
    /// Loads configuration from environment variables.
    /// Throws if any required variable is missing or empty.
    /// </summary>
    public static GrampsConfig FromEnvironment()
    {
        var apiUrl = Environment.GetEnvironmentVariable("GRAMPS_API_URL");
        var username = Environment.GetEnvironmentVariable("GRAMPS_USERNAME");
        var password = Environment.GetEnvironmentVariable("GRAMPS_PASSWORD");
        var refreshToken = Environment.GetEnvironmentVariable("GRAMPS_REFRESH_TOKEN");
        var treeId = Environment.GetEnvironmentVariable("GRAMPS_TREE_ID");
        var readOnly = ParseBoolOrDefault(Environment.GetEnvironmentVariable("GRAMPS_READ_ONLY"), defaultValue: false);
        var mediaResourcesEnabled = ParseBoolOrDefault(
            Environment.GetEnvironmentVariable("GRAMPS_MEDIA_RESOURCES_ENABLED"),
            defaultValue: false);
        var rawMediaMaxBytes = Environment.GetEnvironmentVariable("GRAMPS_MEDIA_MAX_BYTES");
        var mediaMaxBytes = ParseLongOrDefault(rawMediaMaxBytes, DefaultMediaMaxBytes);
        var mediaAllowedMimeTypes = ParseCsvOrDefault(
            Environment.GetEnvironmentVariable("GRAMPS_MEDIA_ALLOWED_MIME_TYPES"),
            DefaultMediaAllowedMimeTypes);
        var mediaAllowPrivate = ParseBoolOrDefault(
            Environment.GetEnvironmentVariable("GRAMPS_MEDIA_ALLOW_PRIVATE"),
            defaultValue: false);
        var mutationSerialize = ParseBoolOrDefault(
            Environment.GetEnvironmentVariable("GRAMPS_MUTATION_SERIALIZE"),
            defaultValue: true);
        var rawMutationMinIntervalMs = Environment.GetEnvironmentVariable("GRAMPS_MUTATION_MIN_INTERVAL_MS");
        var mutationMinIntervalMs = ParseIntOrDefault(rawMutationMinIntervalMs, 0);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(apiUrl))
            errors.Add("GRAMPS_API_URL is not set or empty");
        // A refresh token replaces the username and password entirely.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            if (string.IsNullOrWhiteSpace(username))
                errors.Add("GRAMPS_USERNAME is not set or empty (or set GRAMPS_REFRESH_TOKEN)");
            if (string.IsNullOrWhiteSpace(password))
                errors.Add("GRAMPS_PASSWORD is not set or empty (or set GRAMPS_REFRESH_TOKEN)");
        }
        if (string.IsNullOrWhiteSpace(treeId))
            errors.Add("GRAMPS_TREE_ID is not set or empty");
        if (!string.IsNullOrWhiteSpace(rawMediaMaxBytes) && !long.TryParse(rawMediaMaxBytes, out _))
            errors.Add("GRAMPS_MEDIA_MAX_BYTES must be a valid integer");
        else if (mediaMaxBytes <= 0)
            errors.Add("GRAMPS_MEDIA_MAX_BYTES must be a positive integer");
        if (!string.IsNullOrWhiteSpace(rawMutationMinIntervalMs) && !int.TryParse(rawMutationMinIntervalMs, out _))
            errors.Add("GRAMPS_MUTATION_MIN_INTERVAL_MS must be a valid integer");
        else if (mutationMinIntervalMs < 0)
            errors.Add("GRAMPS_MUTATION_MIN_INTERVAL_MS must be a non-negative integer");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration validation failed:\n" +
                string.Join("\n", errors.Select(e => "  • " + e)));
        }

        return new GrampsConfig(
            ApiUrl: apiUrl!.TrimEnd('/'),
            Username: username ?? string.Empty,
            Password: password ?? string.Empty,
            TreeId: treeId!,
            ReadOnly: readOnly,
            MediaResourcesEnabled: mediaResourcesEnabled,
            MediaMaxBytes: mediaMaxBytes,
            MediaAllowedMimeTypes: mediaAllowedMimeTypes,
            MediaAllowPrivate: mediaAllowPrivate,
            MutationSerialize: mutationSerialize,
            MutationMinIntervalMs: mutationMinIntervalMs,
            RefreshToken: string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim());
    }

    private static bool ParseBoolOrDefault(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private static long ParseLongOrDefault(string? value, long defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return long.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static int ParseIntOrDefault(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return int.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string[] ParseCsvOrDefault(string? value, string[] defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        var items = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return items.Length == 0
            ? defaultValue
            : items;
    }
}
