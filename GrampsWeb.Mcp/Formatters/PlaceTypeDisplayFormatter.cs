using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Maps stored <see cref="Models.GrampsPlace.Type"/> values (plain string or numeric index from mutation payloads) to default place-type labels from the API.
/// </summary>
public static class PlaceTypeDisplayFormatter
{
    /// <summary>
    /// Returns a human-readable place type for MCP tool output. Numeric strings are resolved against <c>/api/types/default/place_types</c>.
    /// </summary>
    public static async Task<string> FormatStoredPlaceTypeAsync(GrampsApiClient client, string? storedType)
    {
        return ResolveStoredPlaceType(storedType, await GetDefaultPlaceTypeLabelsAsync(client));
    }

    /// <summary>
    /// Pure mapping for tests: if <paramref name="labels"/> is null or too short, returns <paramref name="storedType"/> unchanged.
    /// </summary>
    internal static string ResolveStoredPlaceType(string? storedType, IReadOnlyList<string>? labels)
    {
        if (string.IsNullOrWhiteSpace(storedType))
            return "—";

        var t = storedType.Trim();
        if (!IsNumericIndex(t))
            return t;

        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) || idx < 0)
            return t;

        if (labels != null && idx < labels.Count && labels[idx].Length > 0)
            return labels[idx];

        return t;
    }

    internal static bool IsNumericIndex(string t) => t.Length > 0 && t.All(c => c is >= '0' and <= '9');

    private static async Task<IReadOnlyList<string>?> GetDefaultPlaceTypeLabelsAsync(GrampsApiClient client)
    {
        try
        {
            var el = await client.GetAsync<JsonElement>("/api/types/default/place_types");
            if (el.ValueKind == JsonValueKind.Array)
            {
                var list = ParseStringArray(el);
                if (list.Count > 0)
                    return list;
            }
        }
        catch
        {
            // fall through to bulk default types
        }

        try
        {
            var root = await client.GetAsync<JsonElement>("/api/types/default/");
            var categories = TypesPayloadParser.ParseCategories(root);
            foreach (var key in new[] { "place_types", "placeTypes" })
            {
                if (categories.TryGetValue(key, out var list) && list.Count > 0)
                    return list;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static List<string> ParseStringArray(JsonElement el) =>
        el.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText())
            .Where(s => s.Length > 0)
            .ToList();
}
