using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Shared JWT token cache for Gramps API clients.
/// </summary>
public sealed class GrampsAuthTokenProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);

    private readonly HttpClient _httpClient;
    private readonly GrampsConfig _config;
    private readonly ILogger<GrampsAuthTokenProvider> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiration = DateTime.MinValue;

    public GrampsAuthTokenProvider(
        HttpClient httpClient,
        GrampsConfig config,
        ILogger<GrampsAuthTokenProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient.BaseAddress = new Uri(_config.ApiUrl);
        _httpClient.Timeout = RequestTimeout;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (HasUsableAccessToken())
            return _accessToken!;

        await _tokenLock.WaitAsync();
        try
        {
            if (HasUsableAccessToken())
                return _accessToken!;

            await EnsureFreshTokenAsync();

            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> GetTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            await RequestNewTokenAsync();
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<string> RefreshTokenAsync()
    {
        await _tokenLock.WaitAsync();
        try
        {
            if (string.IsNullOrEmpty(_accessToken) && string.IsNullOrEmpty(_refreshToken))
                throw new InvalidOperationException("No token to refresh; call GetTokenAsync() first");

            await EnsureFreshTokenAsync();
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private bool HasUsableAccessToken()
    {
        return !string.IsNullOrEmpty(_accessToken)
               && _tokenExpiration - DateTime.UtcNow >= RefreshSkew;
    }

    private async Task EnsureFreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            await RequestNewTokenAsync();
            return;
        }

        try
        {
            await RefreshCurrentTokenAsync();
        }
        catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "Token refresh rejected with 401; clearing cached tokens and re-authenticating");
            ClearCachedTokens();
            await RequestNewTokenAsync();
        }
    }

    private void ClearCachedTokens()
    {
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiration = DateTime.MinValue;
    }

    private async Task RequestNewTokenAsync()
    {
        _logger.LogDebug("Requesting new JWT token from {Url}", _config.ApiUrl);

        var tokenRequest = new { username = _config.Username, password = _config.Password };
        var json = JsonSerializer.Serialize(tokenRequest, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await SendTokenRequestAsync(HttpMethod.Post, "/api/token/", content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body,
                    $"Failed to obtain token: {response.StatusCode}");
            }

            StoreToken(ParseTokenResponse(body));
            _logger.LogDebug("Token obtained, expires at {Expiration}", _tokenExpiration);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(HttpStatusCode.BadGateway,
                ex.Message, $"Failed to connect to Gramps API: {ex.Message}");
        }
    }

    private async Task RefreshCurrentTokenAsync()
    {
        _logger.LogDebug("Refreshing JWT token");

        var refreshRequest = !string.IsNullOrEmpty(_refreshToken)
            ? new Dictionary<string, string> { ["refresh_token"] = _refreshToken }
            : new Dictionary<string, string> { ["access"] = _accessToken! };
        var json = JsonSerializer.Serialize(refreshRequest, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await SendTokenRequestAsync(HttpMethod.Post, "/api/token/refresh/", content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body,
                    $"Failed to refresh token: {response.StatusCode}");
            }

            StoreToken(ParseTokenResponse(body), keepExistingRefreshToken: true);
            _logger.LogDebug("Token refreshed, expires at {Expiration}", _tokenExpiration);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(HttpStatusCode.BadGateway,
                ex.Message, $"Failed to refresh token: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage> SendTokenRequestAsync(
        HttpMethod method,
        string path,
        HttpContent content)
    {
        _logger.LogInformation("Gramps API auth request: {Method} {Path}", method.Method, path);

        var startedAt = DateTime.UtcNow;
        var response = await _httpClient.SendAsync(new HttpRequestMessage(method, path) { Content = content });
        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

        _logger.LogInformation(
            "Gramps API auth response: {Method} {Path} Status={StatusCode} DurationMs={DurationMs}",
            method.Method,
            path,
            (int)response.StatusCode,
            elapsedMs);

        return response;
    }

    private void StoreToken(TokenResponse tokenResponse, bool keepExistingRefreshToken = false)
    {
        _accessToken = tokenResponse.AccessToken;
        _refreshToken = tokenResponse.RefreshToken
                        ?? (keepExistingRefreshToken ? _refreshToken : null);
        _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 900);
    }

    private static TokenResponse ParseTokenResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = GetString(root, "access_token")
                          ?? GetString(root, "access");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Token response does not contain access token");

        var refreshToken = GetString(root, "refresh_token")
                           ?? GetString(root, "refresh");
        var expiresIn = GetInt(root, "expires_in");

        return new TokenResponse(accessToken, refreshToken, expiresIn);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int? GetInt(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int? ExpiresIn);
}
