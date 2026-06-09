using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// HTTP client for Gramps Web API with JWT authentication and automatic token refresh.
/// </summary>
public class GrampsApiClient
{
    private static readonly string[] SensitiveFieldNames =
    [
        "password", "token", "access", "refresh", "authorization"
    ];

    private readonly HttpClient _httpClient;
    private readonly GrampsConfig _config;
    private readonly ILogger<GrampsApiClient> _logger;

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiration = DateTime.MinValue;

    /// <summary>
    /// Timeout for HTTP requests (30 seconds).
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public GrampsApiClient(HttpClient httpClient, GrampsConfig config, ILogger<GrampsApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Set base address without /api suffix; it's added per endpoint
        _httpClient.BaseAddress = new Uri(_config.ApiUrl);
        _httpClient.Timeout = RequestTimeout;
    }

    /// <summary>
    /// Gets a new JWT access token via POST /api/token/{username}/{password}.
    /// </summary>
    public async Task GetTokenAsync()
    {
        _logger.LogDebug("Requesting new JWT token from {Url}", _config.ApiUrl);

        var tokenRequest = new { username = _config.Username, password = _config.Password };
        var json = JsonSerializer.Serialize(tokenRequest, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await SendWithLoggingAsync(HttpMethod.Post, "/api/token/", content, skipRequestBodyLogging: true);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body,
                    $"Failed to obtain token: {response.StatusCode}");
            }

            var tokenResponse = ParseTokenResponse(body);
            _accessToken = tokenResponse.AccessToken;
            _refreshToken = tokenResponse.RefreshToken;
            // Token expiration is 15 minutes from now
            _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 900);

            _logger.LogDebug("Token obtained, expires at {Expiration}", _tokenExpiration);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway,
                ex.Message, $"Failed to connect to Gramps API: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the JWT access token via POST /api/token/refresh/.
    /// </summary>
    public async Task RefreshTokenAsync()
    {
        if (string.IsNullOrEmpty(_accessToken) && string.IsNullOrEmpty(_refreshToken))
        {
            throw new InvalidOperationException("No token to refresh; call GetTokenAsync() first");
        }

        _logger.LogDebug("Refreshing JWT token");

        var refreshRequest = !string.IsNullOrEmpty(_refreshToken)
            ? new Dictionary<string, string> { ["refresh_token"] = _refreshToken }
            : new Dictionary<string, string> { ["access"] = _accessToken! };
        var json = JsonSerializer.Serialize(refreshRequest, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await SendWithLoggingAsync(HttpMethod.Post, "/api/token/refresh/", content, skipRequestBodyLogging: true);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body,
                    $"Failed to refresh token: {response.StatusCode}");
            }

            var tokenResponse = ParseTokenResponse(body);
            _accessToken = tokenResponse.AccessToken;
            _refreshToken = tokenResponse.RefreshToken ?? _refreshToken;
            _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 900);

            _logger.LogDebug("Token refreshed, expires at {Expiration}", _tokenExpiration);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway,
                ex.Message, $"Failed to refresh token: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the client is authenticated. Obtains a new token or refreshes if needed.
    /// Refreshes if token expires in less than 2 minutes.
    /// </summary>
    public async Task EnsureAuthenticatedAsync()
    {
        var now = DateTime.UtcNow;
        var timeUntilExpiration = _tokenExpiration - now;

        // If no token or expires in less than 2 minutes, refresh/obtain
        if (string.IsNullOrEmpty(_accessToken) || timeUntilExpiration < TimeSpan.FromMinutes(2))
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                await GetTokenAsync();
            }
            else
            {
                await RefreshTokenAsync();
            }
        }
    }

    /// <summary>
    /// Performs a GET request and deserializes the response to T.
    /// Adds Authorization header and executes request.
    /// </summary>
    public async Task<T> GetAsync<T>(string path)
    {
        await EnsureAuthenticatedAsync();

        var url = path;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body);
            }

            var result = JsonSerializer.Deserialize<T>(body, GrampsJson.Options)
                ?? throw new InvalidOperationException($"Failed to deserialize response as {typeof(T).Name}");

            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Performs a POST request with the given body and deserializes the response to T.
    /// Adds Authorization header and executes request.
    /// </summary>
    public async Task<T> PostAsync<T>(string path, object body)
    {
        await EnsureAuthenticatedAsync();

        var url = path;
        var json = JsonSerializer.Serialize(body, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, responseBody);
            }

            var result = JsonSerializer.Deserialize<T>(responseBody, GrampsJson.Options)
                ?? throw new InvalidOperationException($"Failed to deserialize response as {typeof(T).Name}");

            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// GET for list endpoints that may return either a JSON array or a paged <c>{ objects, total, page }</c> object.
    /// </summary>
    public async Task<GrampsPagedResult<T>> GetPagedListAsync<T>(string path) where T : class
    {
        await EnsureAuthenticatedAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new GrampsApiException(response.StatusCode, body);

            var parsed = GrampsPagedResultParser.Parse<T>(body, GrampsJson.Options)
                ?? new GrampsPagedResult<T> { Objects = Array.Empty<T>(), Total = 0, Page = 1 };

            // Array-shaped responses do not reliably include total/page metadata.
            // Prefer explicit headers when present; otherwise mark total unknown (-1) instead of guessing.
            using (var doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    parsed.Page = GetIntQuery(path, "page") ?? parsed.Page;
                    parsed.Total = TryGetTotalFromHeaders(response.Headers, response.Content.Headers) ?? -1;
                }
            }

            return parsed;
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Performs a PUT request with the given body and deserializes the response to T.
    /// Adds Authorization header and executes request.
    /// </summary>
    public async Task<T> PutAsync<T>(string path, object body)
    {
        await EnsureAuthenticatedAsync();

        var url = path;
        var json = JsonSerializer.Serialize(body, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, responseBody);
            }

            var result = JsonSerializer.Deserialize<T>(responseBody, GrampsJson.Options)
                ?? throw new InvalidOperationException($"Failed to deserialize response as {typeof(T).Name}");

            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// POST to create an object. Returns the <c>handle</c> and <c>gramps_id</c> extracted from the
    /// Gramps mutation-array response (<c>[{"_class":"X","new":{...}}]</c>) or bare entity JSON.
    /// Either value may be null if the API response format is unexpected — callers should treat that
    /// as "created successfully but handle unknown".
    /// </summary>
    public async Task<(string? Handle, string? GrampsId)> PostMutationAsync(
        string path, object body, string grampsClass)
    {
        EnsureWritable();
        await EnsureAuthenticatedAsync();

        var json = JsonSerializer.Serialize(body, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new GrampsApiException(response.StatusCode, responseBody);

            return GrampsMutationParser.ExtractHandleAndId(responseBody, grampsClass);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// PUT to update an object. Throws <see cref="GrampsApiException"/> on any non-success status;
    /// on success returns without parsing the response body.
    /// </summary>
    public async Task PutMutationAsync(string path, object body)
    {
        EnsureWritable();
        await EnsureAuthenticatedAsync();

        var json = JsonSerializer.Serialize(body, GrampsJson.Options);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = content };
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new GrampsApiException(response.StatusCode, responseBody);
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Performs a DELETE request.
    /// Adds Authorization header and executes request.
    /// </summary>
    public async Task DeleteAsync(string path)
    {
        EnsureWritable();
        await EnsureAuthenticatedAsync();

        var url = path;
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        AddAuthorizationHeader(request);

        try
        {
            var response = await SendWithLoggingAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new GrampsApiException(response.StatusCode, body);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new GrampsApiException(System.Net.HttpStatusCode.BadGateway, ex.Message);
        }
    }

    /// <summary>
    /// Adds the JWT Authorization header to the request.
    /// </summary>
    private void AddAuthorizationHeader(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            throw new InvalidOperationException("Not authenticated; call EnsureAuthenticatedAsync() first");
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
    }

    private void EnsureWritable()
    {
        if (_config.ReadOnly)
            throw new InvalidOperationException(
                "Read-only mode is enabled; create, update, and delete tools are disabled.");
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        bool skipRequestBodyLogging = false)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        return await SendWithLoggingAsync(request, skipRequestBodyLogging);
    }

    private async Task<HttpResponseMessage> SendWithLoggingAsync(
        HttpRequestMessage request,
        bool skipRequestBodyLogging = false)
    {
        var requestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync();
        var requestBodyForLog = skipRequestBodyLogging
            ? "<redacted>"
            : SanitizeAndLimitForLog(requestBody);

        _logger.LogInformation(
            "Gramps API request: {Method} {Path} Body={RequestBody}",
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            requestBodyForLog ?? "<empty>");

        var startedAt = DateTime.UtcNow;
        var response = await _httpClient.SendAsync(request);
        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
        var responseBody = await response.Content.ReadAsStringAsync();
        var responseBodyForLog = SanitizeAndLimitForLog(responseBody);

        _logger.LogInformation(
            "Gramps API response: {Method} {Path} Status={StatusCode} DurationMs={DurationMs} Body={ResponseBody}",
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            (int)response.StatusCode,
            elapsedMs,
            responseBodyForLog ?? "<empty>");

        // Rebuild content so callers can read response body.
        var restoredContent = new StringContent(responseBody, Encoding.UTF8);
        if (response.Content.Headers.ContentType is not null)
            restoredContent.Headers.ContentType = response.Content.Headers.ContentType;

        foreach (var header in response.Content.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            restoredContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = restoredContent;
        return response;
    }

    private static string? SanitizeAndLimitForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var sanitized = RedactSensitiveData(value);
        const int maxLength = 2000;
        return sanitized.Length <= maxLength
            ? sanitized
            : $"{sanitized[..maxLength]}...(truncated)";
    }

    private static string RedactSensitiveData(string input)
    {
        try
        {
            var node = JsonNode.Parse(input);
            if (node is null)
                return input;

            RedactNode(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            // Keep non-JSON payloads readable in logs.
            return input;
        }
    }

    private static void RedactNode(JsonNode node, string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName) && IsSensitiveField(propertyName))
        {
            if (node is JsonValue)
            {
                node.ReplaceWith(JsonValue.Create("***redacted***"));
            }
            else
            {
                // Entire subtree contains sensitive value.
                node.ReplaceWith(JsonValue.Create("***redacted***"));
            }
            return;
        }

        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is not null)
                    RedactNode(kvp.Value, kvp.Key);
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                    RedactNode(item, propertyName);
            }
        }
    }

    private static bool IsSensitiveField(string key)
    {
        var lowered = key.ToLowerInvariant();
        return SensitiveFieldNames.Any(lowered.Contains);
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

    private static int? GetIntQuery(string path, string key)
    {
        var idx = path.IndexOf('?', StringComparison.Ordinal);
        if (idx < 0 || idx >= path.Length - 1)
            return null;

        var query = path[(idx + 1)..];
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = segment.Split('=', 2);
            if (kv.Length != 2)
                continue;
            if (!string.Equals(Uri.UnescapeDataString(kv[0]), key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (int.TryParse(Uri.UnescapeDataString(kv[1]), out var val))
                return val;
        }

        return null;
    }

    private static int? TryGetTotalFromHeaders(
        System.Net.Http.Headers.HttpResponseHeaders headers,
        System.Net.Http.Headers.HttpContentHeaders contentHeaders)
    {
        // Common conventions used by REST APIs.
        var candidates = new[] { "X-Total-Count", "X-Total", "Total-Count" };
        foreach (var name in candidates)
        {
            if (headers.TryGetValues(name, out var vals) &&
                int.TryParse(vals.FirstOrDefault(), out var total))
                return total;
            if (contentHeaders.TryGetValues(name, out vals) &&
                int.TryParse(vals.FirstOrDefault(), out total))
                return total;
        }

        // Content-Range format: "items start-end/total" or "start-end/total"
        if (headers.TryGetValues("Content-Range", out var ranges) || contentHeaders.TryGetValues("Content-Range", out ranges))
        {
            var v = ranges.FirstOrDefault();
            if (!string.IsNullOrEmpty(v))
            {
                var slash = v.LastIndexOf('/');
                if (slash >= 0 && slash < v.Length - 1 &&
                    int.TryParse(v[(slash + 1)..].Trim(), out var total))
                    return total;
            }
        }

        return null;
    }

    /// <summary>
    /// DTO for token response from /api/token/ and /api/token/refresh/.
    /// Supports both snake_case and legacy field names via ParseTokenResponse.
    /// </summary>
    private record TokenResponse(string AccessToken, string? RefreshToken, int? ExpiresIn);
}
