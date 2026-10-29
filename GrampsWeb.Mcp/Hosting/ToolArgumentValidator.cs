using System.Globalization;
using System.Text.Json;
using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Checks tool call arguments against the tool's input schema. The SDK answers a missing required
/// argument or a value it cannot convert with a bare "An error occurred invoking", and ignores
/// unknown names, so a misspelled optional argument would silently do nothing. The SDK also rejects
/// a JSON number for a string parameter, which clients send for a year-only date such as 1877.
/// An update with nothing to change is refused, as it usually means a misspelled argument.
/// </summary>
internal static class ToolArgumentValidator
{
    /// <summary>
    /// Unknown and missing required argument names, also inside the objects of an array argument whose
    /// items the schema describes (people[1].birthDate); null when every name matches the schema.
    /// </summary>
    public static string? CheckNames(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        var unknown = new List<string>();
        var missing = new List<string>();
        var described = new List<string>();
        CheckNames(schema, arguments ?? new Dictionary<string, JsonElement>(), "", null, unknown, missing, described);
        if (unknown.Count == 0 && missing.Count == 0)
            return null;

        var problems = new List<string>();
        if (unknown.Count > 0)
            problems.Add($"Unknown argument{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown)}.");
        if (missing.Count > 0)
            problems.Add($"Missing required argument{(missing.Count > 1 ? "s" : "")}: {string.Join(", ", missing)}.");
        problems.AddRange(described.Distinct());
        return string.Join(" ", problems);
    }

    private static void CheckNames(JsonElement schema, IEnumerable<KeyValuePair<string, JsonElement>> arguments,
        string path, string? arrayName, List<string> unknown, List<string> missing, List<string> described)
    {
        var properties = Properties(schema);
        var given = arguments.ToList();
        var found = unknown.Count + missing.Count;
        unknown.AddRange(given
            .Where(argument => !properties.ContainsKey(argument.Key))
            .Select(argument => Suggest(argument.Key, properties.Keys) is { } match
                ? $"{path}{argument.Key} (did you mean {match}?)"
                : path + argument.Key));
        // The SDK lists a required string of an array item as nullable, so null counts as missing there.
        missing.AddRange(Required(schema)
            .Where(name => given.All(argument => argument.Key != name)
                || given.First(argument => argument.Key == name).Value.ValueKind == JsonValueKind.Null
                && (arrayName != null || !AllowsNull(properties.GetValueOrDefault(name))))
            .Select(name => path + name));
        if (unknown.Count + missing.Count > found)
            described.Add(DescribeParameters(schema, properties, arrayName));

        foreach (var (name, value) in given)
        {
            if (properties.TryGetValue(name, out var property) && ItemObjects(property, value) is { } items)
            {
                foreach (var (index, item) in items)
                    CheckNames(Items(property), item.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value)),
                        $"{path}{name}[{index}].", name, unknown, missing, described);
            }
        }
    }

    /// <summary>
    /// An error for an update_* call that gives no field to change besides handle and linkMode;
    /// null otherwise. Clients such as Claude Code drop arguments the schema does not list, so
    /// update_event(place: …) arrives as update_event(handle) and would save the record unchanged.
    /// </summary>
    public static string? CheckSomethingToUpdate(string toolName, JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (!toolName.StartsWith("update_", StringComparison.Ordinal)
            || arguments?.Any(a => a.Key is not ("handle" or "linkMode") && a.Value.ValueKind != JsonValueKind.Null) == true)
            return null;
        return "Nothing to update: the call gives no field to change. If you passed one, check its name; " +
               "clients drop arguments a tool does not have. " + DescribeParameters(schema, Properties(schema));
    }

    /// <summary>
    /// The arguments with each JSON number given for a string parameter turned into its text
    /// (1877 → "1877"), also inside the objects of an array argument; null when there is none.
    /// </summary>
    public static Dictionary<string, JsonElement>? NumbersAsText(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null)
            return null;
        var properties = Properties(schema);
        Dictionary<string, JsonElement>? converted = null;
        foreach (var (name, value) in arguments)
        {
            if (!properties.TryGetValue(name, out var property) || NumbersAsText(property, value) is not { } text)
                continue;
            converted ??= new Dictionary<string, JsonElement>(arguments);
            converted[name] = text;
        }

        return converted;
    }

    /// <summary>The value with its numbers for string properties as text; null when nothing changes.</summary>
    private static JsonElement? NumbersAsText(JsonElement property, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return Types(property).Contains("string") ? JsonSerializer.SerializeToElement(value.GetRawText()) : null;
        if (ItemObjects(property, value) is not { } items)
            return null;

        JsonElement[]? array = null;
        foreach (var (index, item) in items)
        {
            var itemProperties = Properties(Items(property));
            Dictionary<string, JsonElement>? fields = null;
            foreach (var field in item.EnumerateObject())
            {
                if (!itemProperties.TryGetValue(field.Name, out var fieldProperty)
                    || NumbersAsText(fieldProperty, field.Value) is not { } text)
                    continue;
                fields ??= item.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                fields[field.Name] = text;
            }

            if (fields == null)
                continue;
            array ??= value.EnumerateArray().ToArray();
            array[index] = JsonSerializer.SerializeToElement(fields);
        }

        return array == null ? null : JsonSerializer.SerializeToElement(array);
    }

    /// <summary>
    /// The first argument whose JSON value has a type the schema does not allow, also inside the objects
    /// of an array argument; null when none does. Numbers sent as strings pass, as the SDK converts them.
    /// </summary>
    public static string? FindTypeMismatch(JsonElement schema, IDictionary<string, JsonElement>? arguments) =>
        FindTypeMismatch(schema, arguments ?? new Dictionary<string, JsonElement>(), "", null);

    private static string? FindTypeMismatch(JsonElement schema, IEnumerable<KeyValuePair<string, JsonElement>> arguments,
        string path, string? arrayName)
    {
        var properties = Properties(schema);
        foreach (var (name, value) in arguments)
        {
            if (!properties.TryGetValue(name, out var property))
                continue;
            var types = Types(property);
            if (types.Count == 0 || types.Any(type => Matches(type, value)))
            {
                if (ItemObjects(property, value) is not { } items)
                    continue;
                foreach (var (index, item) in items)
                {
                    if (FindTypeMismatch(Items(property), item.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value)),
                            $"{path}{name}[{index}].", name) is { } nested)
                        return nested;
                }

                continue;
            }

            var expected = string.Join(" or ", types.Where(t => t != "null").Select(t => DescribeType(t, property)));
            return $"Argument {path}{name} must be {Article(expected)}{expected}, not {DescribeKind(value)}. " +
                   DescribeParameters(schema, properties, arrayName);
        }

        return null;
    }

    /// <summary>
    /// The objects of an array value with their indexes, when the schema describes the fields of its items;
    /// null for any other property or value.
    /// </summary>
    private static List<(int Index, JsonElement Item)>? ItemObjects(JsonElement property, JsonElement value) =>
        value.ValueKind == JsonValueKind.Array && Properties(Items(property)).Count > 0
            ? value.EnumerateArray().Select((item, index) => (index, item))
                .Where(i => i.item.ValueKind == JsonValueKind.Object).ToList()
            : null;

    private static JsonElement Items(JsonElement property) =>
        property.ValueKind == JsonValueKind.Object && property.TryGetProperty("items", out var items) ? items : default;

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

    /// <summary>
    /// "Parameters: identifier (string, required), objectType (string), extended (boolean).", or for the
    /// items of an array argument "Fields of people items: …".
    /// </summary>
    private static string DescribeParameters(JsonElement schema, Dictionary<string, JsonElement> properties,
        string? arrayName = null)
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
        return $"{(arrayName == null ? "Parameters" : $"Fields of {arrayName} items")}: {string.Join(", ", parts)}.";
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
