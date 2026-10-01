using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Logging;

/// <summary>
/// Resolves the log level for GrampsWeb.Mcp categories from <c>GRAMPS_LOG_LEVEL</c>.
/// </summary>
public static class GrampsLogLevel
{
    public const string EnvironmentVariable = "GRAMPS_LOG_LEVEL";

    /// <summary>
    /// Parses a level name (Trace, Debug, Information, Warning, Error, Critical, None),
    /// case-insensitive. Blank, unknown, or unresolved <c>${...}</c> values yield Information,
    /// which never logs HTTP bodies or query strings.
    /// </summary>
    public static LogLevel Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LogLevel.Information;

        // Match names only: Enum.TryParse would also accept numbers like "-1".
        var name = Enum.GetNames<LogLevel>()
            .FirstOrDefault(n => n.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null ? LogLevel.Information : Enum.Parse<LogLevel>(name);
    }

    public static LogLevel FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));
}
