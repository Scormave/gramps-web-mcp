using System.Collections.Concurrent;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// In-memory TTL cache for successful Gramps ID to handle resolutions.
/// Thread-safe for concurrent tool calls. Static so tools can use it without DI wiring.
/// </summary>
public static class HandleCache
{
    private sealed record CacheEntry(string Handle, DateTime LoadedAt);

    private static readonly ConcurrentDictionary<string, CacheEntry> Entries = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Builds a cache scope key from API URL and tree ID.
    /// </summary>
    public static string BuildScopeKey(string apiUrl, string treeId) =>
        $"{apiUrl.TrimEnd('/')}|{treeId}";

    /// <summary>
    /// Builds a full cache key from scope and Gramps ID.
    /// </summary>
    public static string BuildCacheKey(string scopeKey, string grampsId) =>
        $"{scopeKey}|{grampsId.ToUpperInvariant()}";

    /// <summary>
    /// Tries to get a cached handle for the given scope and Gramps ID.
    /// Returns <c>null</c> if not cached or expired.
    /// </summary>
    public static string? TryGet(string scopeKey, string grampsId)
    {
        var key = BuildCacheKey(scopeKey, grampsId);
        if (!Entries.TryGetValue(key, out var entry))
            return null;

        if (DateTime.UtcNow - entry.LoadedAt >= CacheTtl)
        {
            Entries.TryRemove(key, out _);
            return null;
        }

        return entry.Handle;
    }

    /// <summary>
    /// Stores a successful Gramps ID to handle mapping.
    /// </summary>
    public static void Set(string scopeKey, string grampsId, string handle)
    {
        var key = BuildCacheKey(scopeKey, grampsId);
        Entries[key] = new CacheEntry(handle, DateTime.UtcNow);
    }

    /// <summary>
    /// Acquires a per-key lock to prevent duplicate concurrent API calls for the same resolution.
    /// Caller must release via <see cref="ReleaseKeyLock"/>.
    /// </summary>
    public static async Task AcquireKeyLockAsync(string scopeKey, string grampsId)
    {
        var key = BuildCacheKey(scopeKey, grampsId);
        var sem = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
    }

    /// <summary>
    /// Releases the per-key lock acquired by <see cref="AcquireKeyLockAsync"/>.
    /// </summary>
    public static void ReleaseKeyLock(string scopeKey, string grampsId)
    {
        var key = BuildCacheKey(scopeKey, grampsId);
        if (KeyLocks.TryGetValue(key, out var sem))
            sem.Release();
    }

    /// <summary>Invalidates all cached entries. Intended for tests and administrative use.</summary>
    public static void Invalidate()
    {
        Entries.Clear();
    }
}
