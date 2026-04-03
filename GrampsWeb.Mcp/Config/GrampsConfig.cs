namespace GrampsWeb.Mcp.Config;

/// <summary>
/// Configuration for Gramps Web API connection.
/// Loaded from environment variables.
/// </summary>
public record GrampsConfig(
    string ApiUrl,
    string Username,
    string Password,
    string TreeId)
{
    /// <summary>
    /// Loads configuration from environment variables.
    /// Throws if any required variable is missing or empty.
    /// </summary>
    public static GrampsConfig FromEnvironment()
    {
        var apiUrl = Environment.GetEnvironmentVariable("GRAMPS_API_URL");
        var username = Environment.GetEnvironmentVariable("GRAMPS_USERNAME");
        var password = Environment.GetEnvironmentVariable("GRAMPS_PASSWORD");
        var treeId = Environment.GetEnvironmentVariable("GRAMPS_TREE_ID");

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(apiUrl))
            errors.Add("GRAMPS_API_URL is not set or empty");
        if (string.IsNullOrWhiteSpace(username))
            errors.Add("GRAMPS_USERNAME is not set or empty");
        if (string.IsNullOrWhiteSpace(password))
            errors.Add("GRAMPS_PASSWORD is not set or empty");
        if (string.IsNullOrWhiteSpace(treeId))
            errors.Add("GRAMPS_TREE_ID is not set or empty");

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
            TreeId: treeId!);
    }
}
