using System.Net;

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

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody)
    {
        var truncated = responseBody.Length > 200
            ? responseBody[..200] + "..."
            : responseBody;

        return $"Gramps API error {(int)statusCode} ({statusCode}): {truncated}";
    }
}
