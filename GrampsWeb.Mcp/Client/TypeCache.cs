using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Merged (default + custom) Gramps type vocabularies and type validation for write tools.
/// The vocabularies themselves are kept by <see cref="GrampsTypeVocabularies"/>.
/// </summary>
public static class TypeCache
{
    /// <summary>
    /// Gets the merged (default + custom) type vocabularies. Custom types are skipped when their
    /// endpoint fails; <paramref name="reloadCustom"/> reads them again instead of using the cache.
    /// </summary>
    public static async Task<Dictionary<string, IReadOnlyList<string>>> GetTypesAsync(
        GrampsApiClient client,
        bool reloadCustom = false)
    {
        var types = TypesPayloadParser.ParseCategories(await client.GetDefaultTypesAsync());

        try
        {
            var customTypes = TypesPayloadParser.ParseCategories(await client.GetCustomTypesAsync(reloadCustom));

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

        return types;
    }

    /// <summary>
    /// Validates a type string against a specific category (e.g. "event_types").
    /// Returns <c>null</c> if valid, or an error message with suggestions if invalid.
    /// Comparison is case-insensitive. An unknown value reloads custom types once, so a type
    /// just added in Gramps is accepted.
    /// </summary>
    public static async Task<string?> ValidateTypeAsync(string value, string category, GrampsApiClient client)
    {
        if (IsValid(await GetTypesAsync(client), value, category))
            return null;

        var types = await GetTypesAsync(client, reloadCustom: true);
        if (IsValid(types, value, category))
            return null;

        var candidates = types[category];
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

    // An unknown category skips validation rather than blocking the write.
    private static bool IsValid(Dictionary<string, IReadOnlyList<string>> types, string value, string category) =>
        !types.TryGetValue(category, out var candidates)
        || candidates.Count == 0
        || candidates.Any(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase));

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

    internal static int LevenshteinDistance(string a, string b)
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
