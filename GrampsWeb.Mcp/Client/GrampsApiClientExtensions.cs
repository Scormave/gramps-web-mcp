using System.Net;
using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Helpers for tools that should return a friendly message when Gramps returns HTTP 404.
/// </summary>
public static class GrampsApiClientExtensions
{
    /// <summary>
    /// Like <see cref="GrampsApiClient.GetAsync{T}"/>, but returns <c>null</c> on HTTP 404 instead of throwing.
    /// </summary>
    public static async Task<T?> GetOrNullIfNotFoundAsync<T>(this GrampsApiClient client, string path)
        where T : class
    {
        try
        {
            return await client.GetAsync<T>(path);
        }
        catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Like <see cref="GrampsApiClient.GetAsync{T}"/> for <see cref="JsonElement"/>, but returns <c>null</c> on HTTP 404.
    /// </summary>
    public static async Task<JsonElement?> GetJsonOrNullIfNotFoundAsync(this GrampsApiClient client, string path)
    {
        try
        {
            return await client.GetAsync<JsonElement>(path);
        }
        catch (GrampsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
