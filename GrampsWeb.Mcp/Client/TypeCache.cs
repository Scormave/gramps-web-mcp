using System.Text.Json;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// In-memory cache of Gramps type vocabularies (default + custom) with lazy loading and TTL.
/// Thread-safe for concurrent tool calls. Static so tools (which are static classes) can use it
/// without DI wiring.
/// </summary>
public static class TypeCache
{
    private static Dictionary<string, IReadOnlyList<string>>? _types;
    private static DateTime _loadedAt = DateTime.MinValue;
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the merged (default + custom) type vocabularies, loading from the API if the cache
    /// is missing or stale.
    /// </summary>
    public static async Task<Dictionary<string, IReadOnlyList<string>>> GetTypesAsync(GrampsApiClient client)
    {
        if (_types is not null && DateTime.UtcNow - _loadedAt < CacheTtl)
            return _types;

        await Lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock.
            if (_types is not null && DateTime.UtcNow - _loadedAt < CacheTtl)
                return _types;

            var defaultRoot = await client.GetAsync<JsonElement>("/api/types/default/");
            var types = TypesPayloadParser.ParseCategories(defaultRoot);

            try
            {
                var customRoot = await client.GetAsync<JsonElement>("/api/types/custom/");
                var customTypes = TypesPayloadParser.ParseCategories(customRoot);

                foreach (var kvp in customTypes)
                {
                    if (types.TryGetValue(kvp.Key, out var existing))
                    {
                        var merged = existing.ToList();
                        merged.AddRange(kvp.Value);
                        types[kvp.Key] = merged;
                    }
                    else
                    {
                        types[kvp.Key] = kvp.Value.ToList();
                    }
                }
            }
            catch
            {
                // Custom types endpoint may not be available; default types are sufficient.
            }

            _types = types;
            _loadedAt = DateTime.UtcNow;
            return _types;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Validates a type string against a specific category (e.g. "event_types").
    /// Returns <c>null</c> if valid, or an error message with suggestions if invalid.
    /// Comparison is case-insensitive.
    /// </summary>
    public static async Task<string?> ValidateTypeAsync(string value, string category, GrampsApiClient client)
    {
        var types = await GetTypesAsync(client);

        if (!types.TryGetValue(category, out var candidates) || candidates.Count == 0)
            return null; // unknown category — skip validation rather than block

        if (candidates.Any(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)))
            return null;

        var suggestions = FindSimilar(value, candidates);
        var suggestionText = suggestions.Count > 0
            ? $" Did you mean: {string.Join(", ", suggestions)}?"
            : "";

        var validPreview = string.Join(", ", candidates.Take(15));
        if (candidates.Count > 15)
            validPreview += ", …";

        var categoryLabel = category.Replace("_", " ");
        return $"Invalid {categoryLabel} '{value}'.{suggestionText} " +
               $"Valid values from gramps://types: {validPreview}";
    }

    /// <summary>Invalidates the cache, forcing a reload on next access.</summary>
    public static void Invalidate()
    {
        _types = null;
        _loadedAt = DateTime.MinValue;
    }

    private static List<string> FindSimilar(string input, IReadOnlyList<string> candidates, int maxResults = 5)
    {
        var scored = new List<(string value, int score)>();
        var inputLower = input.ToLowerInvariant();

        foreach (var candidate in candidates)
        {
            var candidateLower = candidate.ToLowerInvariant();

            if (candidateLower.Contains(inputLower) || inputLower.Contains(candidateLower))
            {
                scored.Add((candidate, 0));
                continue;
            }

            if (candidateLower.StartsWith(inputLower[..Math.Min(3, inputLower.Length)]))
            {
                scored.Add((candidate, 1));
                continue;
            }

            var dist = LevenshteinDistance(inputLower, candidateLower);
            if (dist <= 2)
                scored.Add((candidate, dist));
        }

        return scored
            .OrderBy(s => s.score)
            .ThenBy(s => s.value, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.value)
            .Take(maxResults)
            .ToList();
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
