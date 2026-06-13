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
    bool MediaAllowPrivate = false)
{
    public const long DefaultMediaMaxBytes = 5 * 1024 * 1024;

    public static readonly string[] DefaultMediaAllowedMimeTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf"
    ];

    public string[] EffectiveMediaAllowedMimeTypes =>
        MediaAllowedMimeTypes is { Length: > 0 }
            ? MediaAllowedMimeTypes
            : DefaultMediaAllowedMimeTypes;

    /// <summary>
    /// Loads configuration from environment variables.
    /// Throws if any required variable is missing or empty.
    /// </summary>
    public static GrampsConfig FromEnvironment(string[]? args = null)
    {
        var apiUrl = Environment.GetEnvironmentVariable("GRAMPS_API_URL");
        var username = Environment.GetEnvironmentVariable("GRAMPS_USERNAME");
        var password = Environment.GetEnvironmentVariable("GRAMPS_PASSWORD");
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
        var cliReadOnly = ParseReadOnlyArgument(args ?? []);
        if (cliReadOnly.HasValue)
            readOnly = cliReadOnly.Value;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(apiUrl))
            errors.Add("GRAMPS_API_URL is not set or empty");
        if (string.IsNullOrWhiteSpace(username))
            errors.Add("GRAMPS_USERNAME is not set or empty");
        if (string.IsNullOrWhiteSpace(password))
            errors.Add("GRAMPS_PASSWORD is not set or empty");
        if (string.IsNullOrWhiteSpace(treeId))
            errors.Add("GRAMPS_TREE_ID is not set or empty");
        if (!string.IsNullOrWhiteSpace(rawMediaMaxBytes) && !long.TryParse(rawMediaMaxBytes, out _))
            errors.Add("GRAMPS_MEDIA_MAX_BYTES must be a valid integer");
        else if (mediaMaxBytes <= 0)
            errors.Add("GRAMPS_MEDIA_MAX_BYTES must be a positive integer");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Configuration validation failed:\n" +
                string.Join("\n", errors.Select(e => "  • " + e)));
        }

        return new GrampsConfig(
            ApiUrl: apiUrl!.TrimEnd('/'),
            Username: username!,
            Password: password!,
            TreeId: treeId!,
            ReadOnly: readOnly,
            MediaResourcesEnabled: mediaResourcesEnabled,
            MediaMaxBytes: mediaMaxBytes,
            MediaAllowedMimeTypes: mediaAllowedMimeTypes,
            MediaAllowPrivate: mediaAllowPrivate);
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

    private static bool? ParseReadOnlyArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--read-only", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--gramps-read-only", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryParseReadOnlyAssignment(arg, "--read-only=", out var value)
                || TryParseReadOnlyAssignment(arg, "--gramps-read-only=", out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseReadOnlyAssignment(string arg, string prefix, out bool value)
    {
        value = false;
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var raw = arg[prefix.Length..];
        value = raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("1", StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
