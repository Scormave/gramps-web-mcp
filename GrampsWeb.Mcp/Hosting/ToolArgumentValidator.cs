using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Checks tool call arguments against the tool's input schema. The SDK answers a missing required
/// argument or a value it cannot convert with a bare "An error occurred invoking", and ignores
/// unknown names, so a misspelled optional argument would silently do nothing. The SDK also rejects
/// a JSON number for a string parameter, which clients send for a year-only date such as 1877.
/// </summary>
internal static class ToolArgumentValidator
{
    /// <summary>Unknown and missing required argument names; null when every name matches the schema.</summary>
    public static string? CheckNames(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        var properties = Properties(schema);
        var unknown = (arguments?.Keys ?? [])
            .Where(name => !properties.ContainsKey(name))
            .Select(name => Suggest(name, properties.Keys) is { } match ? $"{name} (did you mean {match}?)" : name)
            .ToList();
        var missing = Required(schema)
            .Where(name => arguments == null
                || !arguments.TryGetValue(name, out var value)
                || value.ValueKind == JsonValueKind.Null && !AllowsNull(properties.GetValueOrDefault(name)))
            .ToList();
        if (unknown.Count == 0 && missing.Count == 0)
            return null;

        var problems = new List<string>();
        if (unknown.Count > 0)
            problems.Add($"Unknown argument{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown)}.");
        if (missing.Count > 0)
            problems.Add($"Missing required argument{(missing.Count > 1 ? "s" : "")}: {string.Join(", ", missing)}.");
        problems.Add(DescribeParameters(schema, properties));
        return string.Join(" ", problems);
    }

    /// <summary>
    /// The arguments with each JSON number given for a string parameter turned into its text
    /// (1877 → "1877"); null when there is none.
    /// </summary>
    public static Dictionary<string, JsonElement>? NumbersAsText(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null)
            return null;
        var properties = Properties(schema);
        Dictionary<string, JsonElement>? converted = null;
        foreach (var (name, value) in arguments)
        {
            if (value.ValueKind != JsonValueKind.Number
                || !properties.TryGetValue(name, out var property)
                || !Types(property).Contains("string"))
                continue;
            converted ??= new Dictionary<string, JsonElement>(arguments);
            converted[name] = JsonSerializer.SerializeToElement(value.GetRawText());
        }

        return converted;
    }

    /// <summary>
    /// The first argument whose JSON value has a type the schema does not allow; null when none does.
    /// Numbers sent as strings pass, as the SDK converts them.
    /// </summary>
    public static string? FindTypeMismatch(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        var properties = Properties(schema);
        foreach (var (name, value) in arguments ?? new Dictionary<string, JsonElement>())
        {
            if (!properties.TryGetValue(name, out var property))
                continue;
            var types = Types(property);
            if (types.Count == 0 || types.Any(type => Matches(type, value)))
                continue;
            var expected = string.Join(" or ", types.Where(t => t != "null").Select(t => DescribeType(t, property)));
            return $"Argument {name} must be {Article(expected)}{expected}, not {DescribeKind(value)}. " +
                   DescribeParameters(schema, properties);
        }

        return null;
    }

    private static Dictionary<string, JsonElement> Properties(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("properties", out var properties)
        && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal)
            : [];

    private static List<string> Required(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object
        && schema.TryGetProperty("required", out var required)
        && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToList()
            : [];

    /// <summary>The schema's types: "string", or ["string", "null"]; empty for an untyped property.</summary>
    private static List<string> Types(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object || !property.TryGetProperty("type", out var type))
            return [];
        return type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => type.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToList(),
            _ => []
        };
    }

    private static bool AllowsNull(JsonElement property) =>
        Types(property) is var types && (types.Count == 0 || types.Contains("null"));

    private static bool Matches(string type, JsonElement value) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _)
            || value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => value.ValueKind == JsonValueKind.Number
            || value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true
    };

    /// <summary>"Parameters: identifier (string, required), objectType (string), extended (boolean)."</summary>
    private static string DescribeParameters(JsonElement schema, Dictionary<string, JsonElement> properties)
    {
        if (properties.Count == 0)
            return "This tool takes no arguments.";
        var required = Required(schema).ToHashSet(StringComparer.Ordinal);
        var parts = properties.Select(p =>
        {
            var details = Types(p.Value).Where(t => t != "null").Select(t => DescribeType(t, p.Value)).ToList();
            if (required.Contains(p.Key))
                details.Add("required");
            return details.Count == 0 ? p.Key : $"{p.Key} ({string.Join(", ", details)})";
        });
        return $"Parameters: {string.Join(", ", parts)}.";
    }

    /// <summary>"array of strings" for a typed array, otherwise the type name.</summary>
    private static string DescribeType(string type, JsonElement property)
    {
        if (type != "array" || !property.TryGetProperty("items", out var items))
            return type;
        var itemType = Types(items).FirstOrDefault(t => t != "null");
        return itemType == null ? "array" : $"array of {itemType}s";
    }

    private static string DescribeKind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"the string {JsonSerializer.Serialize(Shorten(value.GetString()!))}",
        JsonValueKind.Number => $"the number {Shorten(value.GetRawText())}",
        JsonValueKind.True or JsonValueKind.False => $"the boolean {value.GetRawText()}",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => "null"
    };

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";

    private static string Article(string noun) => noun.Length > 0 && "aeiou".Contains(noun[0]) ? "an " : "a ";

    /// <summary>
    /// The parameter a misspelled name most likely means: the same name ignoring case and
    /// underscores (object_type → objectType), a close spelling, or a prefix (extend → extended).
    /// </summary>
    private static string? Suggest(string name, IEnumerable<string> candidates)
    {
        var key = Normalize(name);
        var scored = candidates
            .Select(candidate => (candidate, normalized: Normalize(candidate)))
            .Select(c => (c.candidate, score:
                c.normalized == key ? 0
                : key.Length >= 4 && TypeCache.LevenshteinDistance(key, c.normalized) <= 2 ? 1
                : key.Length >= 4 && (c.normalized.StartsWith(key, StringComparison.Ordinal)
                    || key.StartsWith(c.normalized, StringComparison.Ordinal)) ? 2
                : int.MaxValue))
            .Where(c => c.score != int.MaxValue)
            .OrderBy(c => c.score)
            .ToList();
        return scored.Count > 0 ? scored[0].candidate : null;
    }

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
