using System.Text.Json;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Gramps Web POST/PUT often return a JSON array of change records:
/// <c>[{ "_class": "Note", "handle": "...", "type": "add", "old": null, "new": { ...entity... } }]</c>
/// instead of a bare entity. This parser unwraps <c>new</c> and deserializes to <typeparamref name="T"/>.
/// </summary>
internal static class GrampsMutationParser
{
    /// <param name="expectedGrampsClass">
    /// When set, selects the first array element whose <c>_class</c> matches (e.g. <c>"Note"</c>).
    /// When null, uses the first element of the array.
    /// </param>
    public static T ExtractNewObject<T>(string json, string? expectedGrampsClass = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "Empty or whitespace response body from Gramps API; expected a mutation payload or entity JSON.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var options = GrampsJson.Options;

        try
        {
            return root.ValueKind switch
            {
                JsonValueKind.Array => ExtractFromChangeArray<T>(root, expectedGrampsClass, options),
                JsonValueKind.Object => ExtractFromObject<T>(root, options),
                _ => throw new InvalidOperationException(
                    $"Unexpected JSON root for mutation response: {root.ValueKind}. Body starts with: {Truncate(json)}")
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse Gramps mutation response as JSON. Body starts with: {Truncate(json)}", ex);
        }
    }

    private static T ExtractFromChangeArray<T>(
        JsonElement array,
        string? expectedGrampsClass,
        JsonSerializerOptions options)
    {
        var len = array.GetArrayLength();
        if (len == 0)
        {
            throw new InvalidOperationException(
                "Gramps mutation response is an empty array; expected at least one change with a \"new\" object.");
        }

        if (expectedGrampsClass != null)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (item.TryGetProperty("_class", out var cls) && cls.ValueKind == JsonValueKind.String
                    && string.Equals(cls.GetString(), expectedGrampsClass, StringComparison.Ordinal))
                {
                    return DeserializeNew<T>(item, options);
                }
            }

            throw new InvalidOperationException(
                $"No mutation entry with _class \"{expectedGrampsClass}\" in response array (length {len}).");
        }

        var first = array[0];
        if (first.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "First element of mutation response array is not a JSON object.");
        }

        return DeserializeNew<T>(first, options);
    }

    private static T ExtractFromObject<T>(JsonElement root, JsonSerializerOptions options)
    {
        if (root.TryGetProperty("new", out var newEl) && newEl.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            return DeserializeNewElement<T>(newEl, options);
        }

        var entity = JsonSerializer.Deserialize<T>(root.GetRawText(), options);
        if (entity == null)
            throw new InvalidOperationException($"Failed to deserialize response as {typeof(T).Name}.");
        return entity;
    }

    private static T DeserializeNew<T>(JsonElement changeItem, JsonSerializerOptions options)
    {
        if (!changeItem.TryGetProperty("new", out var newEl) || newEl.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException(
                "Mutation entry is missing a non-null \"new\" property with the created/updated entity.");
        }

        return DeserializeNewElement<T>(newEl, options);
    }

    private static T DeserializeNewElement<T>(JsonElement newEl, JsonSerializerOptions options)
    {
        var entity = JsonSerializer.Deserialize<T>(newEl.GetRawText(), options);
        if (entity == null)
            throw new InvalidOperationException($"Failed to deserialize mutation \"new\" object as {typeof(T).Name}.");
        return entity;
    }

    private static string Truncate(string s, int max = 120) =>
        s.Length <= max ? s : s[..max] + "...";
}
