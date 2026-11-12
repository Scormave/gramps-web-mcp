namespace GrampsWeb.Mcp.Health;

/// <summary>
/// Result of verifying reachability and authentication against the Gramps Web API. A server older than
/// <see cref="Client.GrampsServerVersion.Minimum"/> is reachable but not healthy; <see cref="Error"/> says why.
/// </summary>
public sealed record GrampsConnectivityStatus(
    bool IsHealthy,
    string ApiUrl,
    string ConfiguredTreeId,
    string? TreeName = null,
    string? TreeDatabaseId = null,
    string? GrampsVersion = null,
    string? Error = null,
    string? ApiVersion = null);
