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
    /// Checks a type string against a specific category (e.g. "event_types") and returns the vocabulary's own
    /// spelling of it, so a value sent in another case is stored as the known type; <paramref name="value"/> itself
    /// when the category is unknown or empty. An invalid value gets an error message with suggestions instead.
    /// Comparison is case-insensitive. An unknown value reloads custom types once, so a type just added in Gramps is
    /// accepted.
    /// </summary>
    public static Task<(string? Label, string? Error)> ResolveTypeAsync(string value, string category, GrampsApiClient client) =>
        ResolveTypeAsync(value, [category], client);

    /// <summary>
    /// Like <see cref="ResolveTypeAsync(string, string, GrampsApiClient)"/> for a vocabulary that Gramps splits over
    /// several categories, e.g. the standard <c>attribute_types</c> and the custom <c>person_attribute_types</c>.
    /// The error names the first category.
    /// </summary>
    public static async Task<(string? Label, string? Error)> ResolveTypeAsync(
        string value, IReadOnlyList<string> categories, GrampsApiClient client)
    {
        if (Find(await GetTypesAsync(client), value, categories) is { } label)
            return (label, null);

        var types = await GetTypesAsync(client, reloadCustom: true);
        if (Find(types, value, categories) is { } reloaded)
            return (reloaded, null);

        var candidates = Candidates(types, categories);
        var suggestions = FindSimilar(value, candidates);
        var suggestionText = suggestions.Count > 0
            ? $" Did you mean: {string.Join(", ", suggestions.Select(s => s.Trim()))}?"
            : "";

        var validPreview = string.Join(", ", candidates.Take(15).Select(c => c.Trim()));
        if (candidates.Count > 15)
            validPreview += ", …";

        var categoryLabel = categories[0].Replace("_", " ");
        return (null, $"Invalid {categoryLabel} '{value}'.{suggestionText} " +
                      $"Valid values from gramps://types: {validPreview}");
    }

    // No known values (unknown or empty categories) skips validation rather than blocking the write. Values are
    // compared trimmed, since Gramps spells the unknown surname origin "Unknown " and expects it back that way.
    private static string? Find(Dictionary<string, IReadOnlyList<string>> types, string value, IReadOnlyList<string> categories)
    {
        var candidates = Candidates(types, categories);
        return candidates.Count == 0
            ? value
            : candidates.FirstOrDefault(c => string.Equals(c.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Candidates(Dictionary<string, IReadOnlyList<string>> types, IReadOnlyList<string> categories) =>
        categories
            .SelectMany(category => types.TryGetValue(category, out var values) ? values : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static List<string> FindSimilar(string input, IReadOnlyList<string> candidates, int maxResults = 5)
    {
        var scored = new List<(string value, int score)>();
        var inputLower = input.ToLowerInvariant();

        foreach (var candidate in candidates)
        {
            var candidateLower = candidate.Trim().ToLowerInvariant();

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
