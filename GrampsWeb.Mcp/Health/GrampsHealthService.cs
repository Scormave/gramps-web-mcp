using System.Net.Http.Headers;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Health;

/// <summary>
/// Verifies that the configured Gramps Web API is reachable and accepts credentials. The token comes from
/// the shared <see cref="GrampsAuthTokenProvider"/>, so a check logs in only when the cached token is
/// missing, expiring, or rejected; Gramps Web rate-limits its token route.
/// </summary>
public sealed class GrampsHealthService
{
    private readonly HttpClient _httpClient;
    private readonly GrampsConfig _config;
    private readonly GrampsAuthTokenProvider _tokenProvider;
    private readonly ILogger<GrampsHealthService> _logger;

    public GrampsHealthService(
        HttpClient httpClient,
        GrampsConfig config,
        GrampsAuthTokenProvider tokenProvider,
        ILogger<GrampsHealthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _tokenProvider = tokenProvider;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_config.ApiUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<GrampsConnectivityStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metadata = await GetMetadataAsync(cancellationToken);
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
        catch (Exception ex) when (ex is GrampsApiException or HttpRequestException or TaskCanceledException
                                       or JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Gramps Web connectivity check failed for {ApiUrl}", _config.ApiUrl);

            return new GrampsConnectivityStatus(
                IsHealthy: false,
                ApiUrl: _config.ApiUrl,
                ConfiguredTreeId: _config.TreeId,
                Error: ex.Message);
        }
    }

    private async Task<JsonElement> GetMetadataAsync(CancellationToken cancellationToken)
    {
        var accessToken = await _tokenProvider.GetAccessTokenAsync();
        var body = await ReadMetadataAsync(accessToken, cancellationToken);
        // The server rejected the cached token, e.g. after its SECRET_KEY changed: replace it once.
        body ??= await ReadMetadataAsync(
                     await _tokenProvider.ReplaceRejectedAccessTokenAsync(accessToken), cancellationToken)
                 ?? throw new InvalidOperationException("Failed to read metadata: the access token was rejected");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// The metadata body, or null when the server rejects the token; see
    /// <see cref="GrampsAuthTokenProvider.IsRejectedAccessTokenAsync"/>.
    /// </summary>
    private async Task<string?> ReadMetadataAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/metadata/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (await GrampsAuthTokenProvider.IsRejectedAccessTokenAsync(response))
            return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to read metadata: {response.StatusCode}");

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string? TryGetString(JsonElement root, string objectProperty, string childProperty)
    {
        if (!root.TryGetProperty(objectProperty, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;

        if (!obj.TryGetProperty(childProperty, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return value.GetString();
    }
}
