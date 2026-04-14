using System.Net;
using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Helpers for tools that should return a friendly message when Gramps returns HTTP 404.
/// Also provides overloads that transparently resolve Gramps IDs to opaque handles.
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

    /// <summary>
    /// Like <see cref="GetOrNullIfNotFoundAsync{T}"/> but first resolves Gramps IDs (e.g. I0001)
    /// to opaque handles before making the request.
    /// </summary>
    /// <param name="client">The API client.</param>
    /// <param name="handleOrId">An opaque handle or a Gramps ID such as <c>I0001</c>.</param>
    /// <param name="apiPathTemplate">
    /// API path with <c>{0}</c> placeholder for the resolved handle, e.g. <c>/api/people/{0}</c>.
    /// </param>
    public static async Task<T?> GetByHandleOrIdAsync<T>(this GrampsApiClient client, string handleOrId, string apiPathTemplate)
        where T : class
    {
        var resolved = await HandleResolver.ResolveToHandleAsync(handleOrId, client);
        return await client.GetOrNullIfNotFoundAsync<T>(string.Format(apiPathTemplate, Uri.EscapeDataString(resolved)));
    }

    /// <summary>
    /// Like <see cref="GetJsonOrNullIfNotFoundAsync"/> but first resolves Gramps IDs (e.g. I0001)
    /// to opaque handles before making the request.
    /// </summary>
    /// <inheritdoc cref="GetByHandleOrIdAsync{T}" path="/param"/>
    public static async Task<JsonElement?> GetJsonByHandleOrIdAsync(this GrampsApiClient client, string handleOrId, string apiPathTemplate)
    {
        var resolved = await HandleResolver.ResolveToHandleAsync(handleOrId, client);
        return await client.GetJsonOrNullIfNotFoundAsync(string.Format(apiPathTemplate, Uri.EscapeDataString(resolved)));
    }
}
