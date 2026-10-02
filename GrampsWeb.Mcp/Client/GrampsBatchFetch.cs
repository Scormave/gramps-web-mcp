using System.Collections.Concurrent;
using System.Net;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Loads many objects of one type with the list endpoint's <c>handles</c> filter
/// (<c>GET /api/{collection}/?handles=a,b</c>, Gramps Web API 3.14+), falling back to one GET per handle
/// on servers that reject or ignore the filter.
/// </summary>
internal static class GrampsBatchFetch
{
    /// <summary>Handles per list request; keeps the URL well under Gunicorn's 4094-byte request line.</summary>
    internal const int ChunkSize = 50;

    /// <summary>Requests in flight per batch.</summary>
    internal const int MaxConcurrency = 4;

    // Server and tree scopes whose list endpoints rejected or ignored the handles filter.
    private static readonly ConcurrentDictionary<string, byte> HandlesFilterUnsupported = new();

    /// <summary>
    /// Objects by handle; handles that do not exist are left out. <paramref name="query"/> is added to every
    /// request, e.g. <c>profile=self</c>. A single handle uses the plain object route.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, T>> GetByHandlesAsync<T>(
        this GrampsApiClient client,
        string collection,
        IEnumerable<string?> handles,
        Func<T, string?> handleOf,
        string? query = null)
        where T : class
    {
        var wanted = handles
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var found = new ConcurrentDictionary<string, T>(StringComparer.Ordinal);
        if (wanted.Count == 0)
            return found;

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var oneByOne = new ConcurrentQueue<string>();
        if (wanted.Count == 1 || IsFilterUnsupported(client))
        {
            foreach (var h in wanted)
                oneByOne.Enqueue(h);
        }
        else
        {
            await Task.WhenAll(wanted.Chunk(ChunkSize).Select(chunk => ThrottledAsync(gate, async () =>
            {
                foreach (var h in await FetchChunkAsync(client, collection, chunk, handleOf, query, found).ConfigureAwait(false))
                    oneByOne.Enqueue(h);
            }))).ConfigureAwait(false);
        }

        await Task.WhenAll(oneByOne.Select(h => ThrottledAsync(gate, async () =>
        {
            var path = $"/api/{collection}/{Uri.EscapeDataString(h)}" + (query is null ? "" : $"?{query}");
            if (await client.GetOrNullIfNotFoundAsync<T>(path).ConfigureAwait(false) is { } obj)
                found[h] = obj;
        }))).ConfigureAwait(false);

        return found;
    }

    /// <returns>
    /// Handles still to fetch one by one: none when the server applied the filter, because it skips only
    /// handles that do not exist.
    /// </returns>
    private static async Task<IEnumerable<string>> FetchChunkAsync<T>(
        GrampsApiClient client,
        string collection,
        string[] chunk,
        Func<T, string?> handleOf,
        string? query,
        ConcurrentDictionary<string, T> found)
        where T : class
    {
        if (IsFilterUnsupported(client))
            return chunk;

        // page and pagesize bound the reply of a server that ignores handles; the filter itself skips paging.
        var path = $"/api/{collection}/?handles={string.Join(",", chunk.Select(Uri.EscapeDataString))}" +
                   (query is null ? "" : $"&{query}") +
                   $"&page=1&pagesize={chunk.Length}";
        List<T> objects;
        try
        {
            objects = await client.GetAsync<List<T>>(path).ConfigureAwait(false);
        }
        catch (GrampsApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            // Servers that validate query arguments reject the unknown one.
            MarkFilterUnsupported(client);
            return chunk;
        }

        var requested = chunk.ToHashSet(StringComparer.Ordinal);
        var filtered = true;
        foreach (var obj in objects)
        {
            var h = handleOf(obj)?.Trim();
            if (h is not null && requested.Contains(h))
                found[h] = obj;
            else
                filtered = false;
        }

        if (filtered)
            return [];

        // An object nobody asked for: the server ignored the filter and sent an ordinary page.
        MarkFilterUnsupported(client);
        return chunk.Where(h => !found.ContainsKey(h));
    }

    private static bool IsFilterUnsupported(GrampsApiClient client) =>
        HandlesFilterUnsupported.ContainsKey(client.CacheScopeKey);

    private static void MarkFilterUnsupported(GrampsApiClient client) =>
        HandlesFilterUnsupported.TryAdd(client.CacheScopeKey, 0);

    private static async Task ThrottledAsync(SemaphoreSlim gate, Func<Task> work)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
