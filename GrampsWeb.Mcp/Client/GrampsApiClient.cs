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
    private readonly GrampsAuthTokenProvider _tokenProvider;
    private string? _accessToken;

    /// <summary>
    /// Timeout for HTTP requests (30 seconds).
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public GrampsApiClient(
        HttpClient httpClient,
        GrampsConfig config,
        ILogger<GrampsApiClient> logger,
        GrampsAuthTokenProvider tokenProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

        // Set base address without /api suffix; it's added per endpoint
        _httpClient.BaseAddress = new Uri(_config.ApiUrl);
        _httpClient.Timeout = RequestTimeout;
    }

    /// <summary>
    /// Stable key for caches scoped to this client's API URL and configured tree.
    /// </summary>
    public string CacheScopeKey => HandleCache.BuildScopeKey(_config.ApiUrl, _config.TreeId);

    /// <summary>
    /// Gets a new JWT access token via POST /api/token/{username}/{password}.
    /// </summary>
    public async Task GetTokenAsync()
    {
        _accessToken = await _tokenProvider.GetTokenAsync();
    }

    /// <summary>
    /// Refreshes the JWT access token via POST /api/token/refresh/.
    /// </summary>
    public async Task RefreshTokenAsync()
    {
        _accessToken = await _tokenProvider.RefreshTokenAsync();
    }

    /// <summary>
    /// Ensures the client is authenticated. Obtains a new token or refreshes if needed.
    /// Refreshes if token expires in less than 2 minutes.
    /// </summary>
    public async Task EnsureAuthenticatedAsync()
    {
        _accessToken = await _tokenProvider.GetAccessTokenAsync();
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
    /// Performs a binary GET request without logging or converting the response body as text.
    /// </summary>
    public async Task<GrampsBinaryResponse> GetBytesAsync(string path, long maxBytes)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Maximum byte count must be positive.");

        await EnsureAuthenticatedAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AddAuthorizationHeader(request);

        try
        {
            using var response = await SendBinaryWithLoggingAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new GrampsApiException(response.StatusCode, errorBody);
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxBytes)
                throw MediaSizeLimitExceeded(contentLength.Value, maxBytes);

            var bytes = await ReadBytesWithLimitAsync(response.Content, maxBytes);
            var mimeType = response.Content.Headers.ContentType?.MediaType;
            return new GrampsBinaryResponse(bytes, mimeType);
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

    private async Task<HttpResponseMessage> SendBinaryWithLoggingAsync(HttpRequestMessage request)
    {
        _logger.LogInformation(
            "Gramps API binary request: {Method} {Path}",
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty);

        var startedAt = DateTime.UtcNow;
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

        _logger.LogInformation(
            "Gramps API binary response: {Method} {Path} Status={StatusCode} DurationMs={DurationMs} ContentType={ContentType} ContentLength={ContentLength}",
            request.Method.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            (int)response.StatusCode,
            elapsedMs,
            response.Content.Headers.ContentType?.ToString() ?? "<unknown>",
            response.Content.Headers.ContentLength?.ToString() ?? "<unknown>");

        return response;
    }

    private static async Task<byte[]> ReadBytesWithLimitAsync(HttpContent content, long maxBytes)
    {
        await using var stream = await content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
                break;

            total += read;
            if (total > maxBytes)
                throw MediaSizeLimitExceeded(total, maxBytes);

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static InvalidOperationException MediaSizeLimitExceeded(long actualBytes, long maxBytes)
    {
        return new InvalidOperationException(
            $"Media response is {actualBytes} bytes, exceeding the configured limit of {maxBytes} bytes.");
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

}
