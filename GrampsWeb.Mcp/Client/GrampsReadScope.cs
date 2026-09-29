using System.Collections.Concurrent;

namespace GrampsWeb.Mcp.Client;

/// <summary>Deduplicates JSON reads within one read-only MCP call, including in-flight reads.</summary>
internal sealed class GrampsReadScope : IDisposable
{
    private static readonly AsyncLocal<GrampsReadScope?> Ambient = new();
    private readonly GrampsReadScope? _previous;
    private readonly ConcurrentDictionary<(GrampsApiClient Client, string Path), Lazy<Task<string>>> _reads = new();

    private GrampsReadScope()
    {
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static GrampsReadScope Begin() => new();

    public static Task<string> ReadAsync(GrampsApiClient client, string path, Func<Task<string>> fetch)
    {
        var scope = Ambient.Value;
        return scope is null ? fetch() : scope.GetAsync(client, path, fetch);
    }

    private async Task<string> GetAsync(GrampsApiClient client, string path, Func<Task<string>> fetch)
    {
        var key = (client, path);
        var read = _reads.GetOrAdd(key, _ => new Lazy<Task<string>>(fetch));
        try
        {
            return await read.Value.ConfigureAwait(false);
        }
        catch
        {
            // Failures must remain retryable; remove only this failed generation.
            _reads.TryRemove(new KeyValuePair<(GrampsApiClient, string), Lazy<Task<string>>>(key, read));
            throw;
        }
    }

    public void Dispose()
    {
        Ambient.Value = _previous;
        _reads.Clear();
    }
}
