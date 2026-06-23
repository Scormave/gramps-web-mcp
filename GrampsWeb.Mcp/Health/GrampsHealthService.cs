using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Health;

/// <summary>
/// Verifies that the configured Gramps Web API is reachable and accepts credentials.
/// </summary>
public sealed class GrampsHealthService
{
    private readonly HttpClient _httpClient;
    private readonly GrampsConfig _config;
    private readonly ILogger<GrampsHealthService> _logger;

    public GrampsHealthService(
        HttpClient httpClient,
        GrampsConfig config,
        ILogger<GrampsHealthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_config.ApiUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<GrampsConnectivityStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            var metadata = await GetMetadataAsync(accessToken, cancellationToken);
            var treeName = TryGetString(metadata, "database", "name");
            var treeDatabaseId = TryGetString(metadata, "database", "id");
            var grampsVersion = TryGetString(metadata, "gramps", "version");

            return new GrampsConnectivityStatus(
                IsHealthy: true,
                ApiUrl: _config.ApiUrl,
                ConfiguredTreeId: _config.TreeId,
                TreeName: treeName,
                TreeDatabaseId: treeDatabaseId,
                GrampsVersion: grampsVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Gramps Web connectivity check failed for {ApiUrl}", _config.ApiUrl);

            return new GrampsConnectivityStatus(
                IsHealthy: false,
                ApiUrl: _config.ApiUrl,
                ConfiguredTreeId: _config.TreeId,
                Error: ex.Message);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokenRequest = new { username = _config.Username, password = _config.Password };
        var json = JsonSerializer.Serialize(tokenRequest, GrampsJson.Options);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("/api/token/", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to obtain token: {response.StatusCode}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accessToken = GetString(root, "access_token") ?? GetString(root, "access");

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Token response does not contain access token");

        return accessToken;
    }

    private async Task<JsonElement> GetMetadataAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metadata/");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to read metadata: {response.StatusCode}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string? TryGetString(JsonElement root, string objectProperty, string childProperty)
    {
        if (!root.TryGetProperty(objectProperty, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;

        if (!obj.TryGetProperty(childProperty, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
