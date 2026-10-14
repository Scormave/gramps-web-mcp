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
    bool MediaAllowPrivate = false,
    bool MutationSerialize = true,
    int MutationMinIntervalMs = 0,
    string? RefreshToken = null,
    IReadOnlyList<string>? MediaUploadDirs = null,
    long MediaUploadMaxBytes = GrampsConfig.DefaultMediaUploadMaxBytes)
{
    public const long DefaultMediaMaxBytes = 5 * 1024 * 1024;

    public const long DefaultMediaUploadMaxBytes = 50 * 1024 * 1024;

    /// <summary>
    /// Variables that earlier versions read. They are ignored now; startup warns when one is still set.
    /// </summary>
    private static readonly string[] RetiredEnvironmentVariables =
    [
        "GRAMPS_MEDIA_ALLOWED_MIME_TYPES"
    ];

    /// <summary>
    /// True when the server authenticates with a long-lived refresh token instead of
    /// a username and password, for Gramps Web instances with local login disabled.
    /// </summary>
    public bool UsesRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    /// <summary>
    /// True when media file access is enabled and <c>GRAMPS_MEDIA_UPLOAD_DIRS</c> names at least one
    /// folder that create_media and update_media may upload files from. Media file access is the one
    /// switch for every file the server handles, read or uploaded.
    /// </summary>
    public bool MediaUploadEnabled => MediaResourcesEnabled && MediaUploadDirs is { Count: > 0 };

    /// <summary>
    /// Loads configuration from environment variables.
    /// Throws if any required variable is missing or empty.
    /// </summary>
    public static GrampsConfig FromEnvironment()
    {
        var apiUrl = ReadEnvironmentVariable("GRAMPS_API_URL");
        var username = ReadEnvironmentVariable("GRAMPS_USERNAME");
        var password = ReadEnvironmentVariable("GRAMPS_PASSWORD");
        var refreshToken = ReadEnvironmentVariable("GRAMPS_REFRESH_TOKEN");
        var treeId = ReadEnvironmentVariable("GRAMPS_TREE_ID");
        var readOnly = ParseBoolOrDefault(ReadEnvironmentVariable("GRAMPS_READ_ONLY"), defaultValue: false);
        var mediaResourcesEnabled = ParseBoolOrDefault(
            ReadEnvironmentVariable("GRAMPS_MEDIA_RESOURCES_ENABLED"),
            defaultValue: false);
        var rawMediaMaxBytes = ReadEnvironmentVariable("GRAMPS_MEDIA_MAX_BYTES");
        var mediaMaxBytes = ParseLongOrDefault(rawMediaMaxBytes, DefaultMediaMaxBytes);
        var mediaAllowPrivate = ParseBoolOrDefault(
            ReadEnvironmentVariable("GRAMPS_MEDIA_ALLOW_PRIVATE"),
            defaultValue: false);
        var mutationSerialize = ParseBoolOrDefault(
            ReadEnvironmentVariable("GRAMPS_MUTATION_SERIALIZE"),
            defaultValue: true);
        var rawMutationMinIntervalMs = ReadEnvironmentVariable("GRAMPS_MUTATION_MIN_INTERVAL_MS");
        var mutationMinIntervalMs = ParseIntOrDefault(rawMutationMinIntervalMs, 0);
        var rawMediaUploadMaxBytes = ReadEnvironmentVariable("GRAMPS_MEDIA_UPLOAD_MAX_BYTES");
        var mediaUploadMaxBytes = ParseLongOrDefault(rawMediaUploadMaxBytes, DefaultMediaUploadMaxBytes);

        var errors = new List<string>();
        var mediaUploadDirs = ParseDirectoryList(ReadEnvironmentVariable("GRAMPS_MEDIA_UPLOAD_DIRS"),
            "GRAMPS_MEDIA_UPLOAD_DIRS", errors);

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
        if (!string.IsNullOrWhiteSpace(rawMediaUploadMaxBytes) && !long.TryParse(rawMediaUploadMaxBytes, out _))
            errors.Add("GRAMPS_MEDIA_UPLOAD_MAX_BYTES must be a valid integer");
        else if (mediaUploadMaxBytes <= 0)
            errors.Add("GRAMPS_MEDIA_UPLOAD_MAX_BYTES must be a positive integer");

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
            MediaAllowPrivate: mediaAllowPrivate,
            MutationSerialize: mutationSerialize,
            MutationMinIntervalMs: mutationMinIntervalMs,
            RefreshToken: string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim(),
            MediaUploadDirs: mediaUploadDirs,
            MediaUploadMaxBytes: mediaUploadMaxBytes);
    }

    /// <summary>
    /// Returns the retired variables that are still set, so startup can tell the user to remove them.
    /// </summary>
    public static IReadOnlyList<string> FindRetiredEnvironmentVariables() =>
        RetiredEnvironmentVariables
            .Where(name => !string.IsNullOrWhiteSpace(ReadEnvironmentVariable(name)))
            .ToArray();

    /// <summary>
    /// Reads an environment variable, treating an unresolved <c>${...}</c> placeholder as unset.
    /// Claude Desktop passes optional MCPB <c>user_config</c> fields the user left blank
    /// through literally (e.g. <c>${user_config.gramps_refresh_token}</c>).
    /// </summary>
    private static string? ReadEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is null)
            return null;

        var trimmed = value.Trim();
        return trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith('}')
            ? null
            : value;
    }

    /// <summary>
    /// Splits a folder list on the platform's path-list separator (<c>:</c>, or <c>;</c> on Windows),
    /// expanding a leading <c>~</c> or <c>${HOME}</c> to the home folder. Each folder must be an absolute
    /// path; whether it exists is checked when a file is uploaded, so a folder created later still works.
    /// </summary>
    private static IReadOnlyList<string>? ParseDirectoryList(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var directories = new List<string>();
        foreach (var entry in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expanded = ExpandHome(entry);
            if (!Path.IsPathFullyQualified(expanded))
            {
                errors.Add($"{name} entries must be absolute paths; \"{entry}\" is not");
                continue;
            }

            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
            if (!directories.Contains(full))
                directories.Add(full);
        }

        return directories.Count > 0 ? directories : null;
    }

    /// <summary>
    /// Replaces a leading <c>~</c> or <c>${HOME}</c> (alone or before a separator) with the user's home folder.
    /// Claude Desktop passes the MCPB default of the upload folder, <c>${HOME}/Downloads</c>, through
    /// without expanding it while the user has not saved the setting.
    /// </summary>
    internal static string ExpandHome(string path)
    {
        foreach (var prefix in HomePrefixes)
        {
            if (path == prefix)
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path.Length > prefix.Length
                && path.StartsWith(prefix, StringComparison.Ordinal)
                && (path[prefix.Length] == '/' || path[prefix.Length] == Path.DirectorySeparatorChar))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    path[(prefix.Length + 1)..]);
        }

        return path;
    }

    private static readonly string[] HomePrefixes = ["~", "${HOME}"];

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
}
