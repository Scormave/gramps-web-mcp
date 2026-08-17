using System.Net;
using System.Net.Http.Headers;

namespace GrampsWeb.Mcp.Exceptions;

/// <summary>
/// Exception thrown when Gramps Web API returns an error.
/// </summary>
public class GrampsApiException : Exception
{
    /// <summary>
    /// HTTP status code returned by the API.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Response body from the API.
    /// </summary>
    public string ResponseBody { get; }

    public GrampsApiException(
        HttpStatusCode statusCode,
        string responseBody,
        string? message = null) : base(
            message ?? BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// Maps mutation HTTP failures, rewriting SQLite lock and 429 responses into
    /// retryable messages that MCP tools surface with <c>isError</c>.
    /// </summary>
    public static GrampsApiException FromMutationResponse(HttpResponseMessage response, string body)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (IsSqliteLock(response.StatusCode, body))
            return new GrampsApiException(response.StatusCode, body, GrampsRetryableWriteErrors.DatabaseLocked());

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfterMs = ReadRetryAfterMs(response.Headers)
                ?? GrampsRetryableWriteErrors.DefaultRateLimitRetryAfterMs;
            return new GrampsApiException(
                response.StatusCode,
                body,
                GrampsRetryableWriteErrors.RateLimited(retryAfterMs));
        }

        return new GrampsApiException(response.StatusCode, body);
    }

    internal static bool IsSqliteLock(HttpStatusCode statusCode, string body)
    {
        if (statusCode is not (HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable))
            return false;
        if (string.IsNullOrEmpty(body))
            return false;

        return body.Contains("database is locked", StringComparison.OrdinalIgnoreCase)
            || body.Contains("sqlite3.OperationalError", StringComparison.OrdinalIgnoreCase);
    }

    internal static int? ReadRetryAfterMs(HttpResponseHeaders headers)
    {
        var retry = headers.RetryAfter;
        if (retry is null)
            return null;

        TimeSpan delay;
        if (retry.Delta is TimeSpan delta)
            delay = delta;
        else if (retry.Date is DateTimeOffset date)
            delay = date - DateTimeOffset.UtcNow;
        else
            return null;

        if (delay <= TimeSpan.Zero)
            return GrampsRetryableWriteErrors.DefaultRetryAfterMs;

        var ms = (int)Math.Ceiling(delay.TotalMilliseconds);
        return ms < 1 ? 1 : ms;
    }

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody)
    {
        var truncated = responseBody.Length > 200
            ? responseBody[..200] + "..."
            : responseBody;

        return $"Gramps API error {(int)statusCode} ({statusCode}): {truncated}";
    }
}
