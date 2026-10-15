using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The rules argument of list_objects: Gramps filter rules, which Gramps Web applies before gql. The shape is
/// checked here, so a mistake is named by its place in the JSON instead of the server's "Filter does not adhere
/// to schema", and the bare 404 the server returns for an unknown rule name is explained from its rule list.
/// </summary>
internal static class FilterRules
{
    /// <summary>Object types that have filter rules, and the Gramps namespace of each.</summary>
    public static readonly IReadOnlyDictionary<string, string> Namespaces =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["people"] = "Person",
            ["families"] = "Family",
            ["events"] = "Event",
            ["places"] = "Place",
            ["citations"] = "Citation",
            ["sources"] = "Source",
            ["repositories"] = "Repository",
            ["media"] = "Media",
            ["notes"] = "Note"
        };

    /// <summary>The namespaces a nested filter can match from each namespace, through the records' links.</summary>
    private static readonly Dictionary<string, string[]> Bridges = new(StringComparer.Ordinal)
    {
        ["Person"] = ["Event", "Family"],
        ["Family"] = ["Event", "Person"],
        ["Event"] = ["Place"],
        ["Citation"] = ["Source"],
        ["Source"] = ["Repository"]
    };

    /// <summary>Gramps Web stops at this depth: the top filter is 0, so four levels of nested filters fit.</summary>
    private const int MaxDepth = 5;

    /// <summary>The rules as Gramps Web takes them, and every rule name with the namespace it belongs to.</summary>
    public sealed record Request(string Json, IReadOnlyList<RuleName> RuleNames);

    public readonly record struct RuleName(string Namespace, string Name);

    /// <summary>
    /// Checks the rules for <paramref name="objectType"/> and writes them as Gramps Web's JSON. An array is the
    /// rules of an "and" filter and a single rule object is a filter of that rule; a JSON string holds either.
    /// </summary>
    public static Request Build(FlexibleFilterRules rules, string objectType)
    {
        if (!Namespaces.TryGetValue(objectType, out var ns))
            throw McpToolErrors.ValidationError(
                $"rules are not available for {objectType}; they work with {string.Join(", ", Namespaces.Keys)}.");

        var root = Parse(rules.Value);
        var names = new List<RuleName>();
        var buffer = new MemoryStream();
        // The JSON goes into a query string, so names and text in any script can stay unescaped.
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            if (root.ValueKind == JsonValueKind.Array
                || (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("name", out _) && !root.TryGetProperty("rules", out _)))
            {
                json.WriteStartObject();
                json.WritePropertyName("rules");
                if (root.ValueKind == JsonValueKind.Array)
                {
                    WriteItems(json, root, ns, "rules", 0, names);
                }
                else
                {
                    json.WriteStartArray();
                    WriteRule(json, root, ns, "rules[0]", names);
                    json.WriteEndArray();
                }

                json.WriteEndObject();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                WriteFilter(json, root, ns, null, "", 0, names);
            }
            else
            {
                throw McpToolErrors.ValidationError(
                    "rules must be a JSON object such as {\"rules\": [{\"name\": \"IsMale\"}]} or an array of rules, " +
                    $"not {Describe(root)}.");
            }
        }

        return new Request(Encoding.UTF8.GetString(buffer.ToArray()), names);
    }

    /// <summary>
    /// A clearer error for a failed list request with rules, or null to keep the original. Gramps Web answers an
    /// unknown rule name with a bare 404, so the names are checked against its rule list; for the rules it could
    /// not apply (400 or 422) its own message is kept without the JSON around it.
    /// </summary>
    public static async Task<string?> ExplainFailureAsync(GrampsApiException error, Request request, GrampsApiClient client)
    {
        if (error.StatusCode == HttpStatusCode.NotFound)
        {
            try
            {
                return await ExplainUnknownRulesAsync(request, client);
            }
            catch (GrampsApiException)
            {
                // Without the rule list the original error stands.
                return null;
            }
        }

        if (error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
            && ServerMessage(error.ResponseBody) is { } message
            && (message.StartsWith("Rule ", StringComparison.Ordinal)
                || message.StartsWith("Filter ", StringComparison.Ordinal)
                || message.StartsWith("Unsupported namespace", StringComparison.Ordinal)))
            return $"Gramps Web could not apply the rules: {message}";

        return null;
    }

    private static async Task<string?> ExplainUnknownRulesAsync(Request request, GrampsApiClient client)
    {
        var problems = new List<string>();
        foreach (var group in request.RuleNames.GroupBy(rule => rule.Namespace))
        {
            var objectType = Namespaces.First(pair => pair.Value == group.Key).Key;
            var catalog = await client.GetAsync<JsonElement>($"/api/filters/{objectType}");
            var known = KnownRuleNames(catalog);
            problems.AddRange(group
                .Select(rule => rule.Name)
                .Distinct(StringComparer.Ordinal)
                .Where(name => !known.Contains(name))
                .Select(name => DescribeUnknown(name, objectType, known)));
        }

        if (problems.Count == 0)
            return null;
        return $"Unknown filter rule{(problems.Count > 1 ? "s" : "")}: {string.Join("; ", problems)}. " +
               "get_reference with topic filter-rules and the type as section (such as people) lists the rules and their values.";
    }

    private static string DescribeUnknown(string name, string objectType, HashSet<string> known)
    {
        if (name == "MatchesQuery")
            return $"MatchesQuery for {objectType} needs Gramps Web API 3.23 or later; use the gql argument instead";

        var lower = name.ToLowerInvariant();
        var similar = known
            .Select(candidate => (candidate, lowerCandidate: candidate.ToLowerInvariant()))
            .Select(c => (c.candidate, score:
                c.lowerCandidate == lower ? 0
                : TypeCache.LevenshteinDistance(lower, c.lowerCandidate) is var distance && distance <= 3 ? distance
                : lower.Length >= 4 && c.lowerCandidate.Contains(lower, StringComparison.Ordinal) ? 4
                : int.MaxValue))
            .Where(c => c.score != int.MaxValue)
            .OrderBy(c => c.score)
            .ThenBy(c => c.candidate, StringComparer.Ordinal)
            .Select(c => c.candidate)
            .Take(3)
            .ToList();
        return similar.Count == 0
            ? $"{name} for {objectType}"
            : $"{name} for {objectType} (did you mean {string.Join(" or ", similar)}?)";
    }

    /// <summary>The rule class names in an answer of <c>/api/filters/{type}</c>.</summary>
    internal static HashSet<string> KnownRuleNames(JsonElement catalog) =>
        catalog.ValueKind == JsonValueKind.Object
        && catalog.TryGetProperty("rules", out var rules)
        && rules.ValueKind == JsonValueKind.Array
            ? rules.EnumerateArray()
                .Where(rule => rule.ValueKind == JsonValueKind.Object
                    && rule.TryGetProperty("rule", out var name) && name.ValueKind == JsonValueKind.String)
                .Select(rule => rule.GetProperty("rule").GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];

    private static JsonElement Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return value;
        try
        {
            using var document = JsonDocument.Parse(value.GetString()!);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw McpToolErrors.ValidationError(
                $"rules is not valid JSON: {ex.Message} Pass an object such as {{\"rules\": [{{\"name\": \"IsMale\"}}]}}.");
        }
    }

    private static void WriteFilter(
        Utf8JsonWriter json, JsonElement filter, string ns, string? parentNs, string path, int depth, List<RuleName> names)
    {
        if (depth >= MaxDepth)
            throw Invalid(path, "is nested too deep; Gramps Web takes at most four levels of nested filters.");

        var ownNs = ns;
        if (filter.TryGetProperty("namespace", out var namespaceValue))
            ownNs = ResolveNamespace(namespaceValue, ns, parentNs, Join(path, "namespace"));

        json.WriteStartObject();
        var hasRules = false;
        foreach (var property in filter.EnumerateObject())
        {
            var at = Join(path, property.Name);
            switch (property.Name)
            {
                case "function":
                    var function = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()!.Trim().ToLowerInvariant()
                        : null;
                    if (function is not ("and" or "or" or "one"))
                        throw Invalid(at, $"must be and, or or one, not {Describe(property.Value)}.");
                    json.WriteString("function", function);
                    break;
                case "invert":
                    json.WriteBoolean("invert", ReadBoolean(property.Value, at));
                    break;
                case "namespace":
                    if (parentNs != null)
                        json.WriteString("namespace", ownNs);
                    break;
                case "rules":
                    if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() == 0)
                        throw Invalid(at, "must be a non-empty array of rules and nested filters.");
                    json.WritePropertyName("rules");
                    WriteItems(json, property.Value, ownNs, at, depth, names);
                    hasRules = true;
                    break;
                default:
                    throw Invalid(path, $"has an unknown key \"{property.Name}\"; a filter takes function, invert, namespace and rules.");
            }
        }

        if (!hasRules)
            throw Invalid(path, "needs rules: a non-empty array of rules and nested filters.");
        json.WriteEndObject();
    }

    private static void WriteItems(Utf8JsonWriter json, JsonElement items, string ns, string path, int depth, List<RuleName> names)
    {
        json.WriteStartArray();
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var at = $"{path}[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
                throw Invalid(at, $"must be a rule {{\"name\": …}} or a nested filter {{\"rules\": […]}}, not {Describe(item)}.");
            if (item.TryGetProperty("name", out _))
                WriteRule(json, item, ns, at, names);
            else if (item.TryGetProperty("rules", out _))
                WriteFilter(json, item, ns, ns, at, depth + 1, names);
            else
                throw Invalid(at, "needs name (a rule) or rules (a nested filter).");
        }

        json.WriteEndArray();
    }

    private static void WriteRule(Utf8JsonWriter json, JsonElement rule, string ns, string path, List<RuleName> names)
    {
        json.WriteStartObject();
        foreach (var property in rule.EnumerateObject())
        {
            var at = Join(path, property.Name);
            switch (property.Name)
            {
                case "name":
                    var name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()!.Trim() : "";
                    if (name.Length == 0)
                        throw Invalid(at, $"must be a rule name such as IsMale, not {Describe(property.Value)}.");
                    json.WriteString("name", name);
                    names.Add(new RuleName(ns, name));
                    break;
                case "values":
                    WriteValues(json, property.Value, at);
                    break;
                case "regex":
                    json.WriteBoolean("regex", ReadBoolean(property.Value, at));
                    break;
                default:
                    throw Invalid(path, $"has an unknown key \"{property.Name}\"; a rule takes name, values and regex.");
            }
        }

        json.WriteEndObject();
    }

    /// <summary>Writes the values as an array; one value alone counts as an array of it.</summary>
    private static void WriteValues(Utf8JsonWriter json, JsonElement values, string path)
    {
        if (values.ValueKind == JsonValueKind.Null)
            return;

        json.WritePropertyName("values");
        json.WriteStartArray();
        if (values.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var value in values.EnumerateArray())
                WriteValue(json, value, $"{path}[{index++}]");
        }
        else
        {
            WriteValue(json, values, path);
        }

        json.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter json, JsonElement value, string path)
    {
        if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            throw Invalid(path, $"must be text, a number or true/false, not {Describe(value)}.");
        value.WriteTo(json);
    }

    /// <summary>
    /// The Gramps namespace a nested filter names, given as a Gramps namespace (Event) or an object type (events).
    /// </summary>
    private static string ResolveNamespace(JsonElement value, string ns, string? parentNs, string path)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
        var resolved = Namespaces.TryGetValue(text, out var fromType)
            ? fromType
            : Namespaces.Values.FirstOrDefault(n => n.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (resolved == null)
            throw Invalid(path, $"must be one of {string.Join(", ", Namespaces.Values)}, not {Describe(value)}.");

        if (parentNs == null)
        {
            if (resolved != ns)
                throw Invalid(path, $"must be the listed type ({ns}) on the top filter; give namespace on a nested filter instead.");
            return resolved;
        }

        if (resolved != parentNs && !(Bridges.TryGetValue(parentNs, out var reachable) && reachable.Contains(resolved)))
        {
            var options = Bridges.TryGetValue(parentNs, out var targets)
                ? $"a nested filter in {parentNs} can match {string.Join(" or ", targets)}"
                : $"a nested filter in {parentNs} cannot change namespace";
            throw Invalid(path, $"{resolved} cannot be matched from {parentNs}; {options}.");
        }

        return resolved;
    }

    private static bool ReadBoolean(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Invalid(path, $"must be true or false, not {Describe(value)}.")
    };

    /// <summary>The message in a Gramps Web error body, <c>{"error": {"message": …}}</c>.</summary>
    private static string? ServerMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("message", out var message)
                   && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Exception Invalid(string path, string problem) =>
        McpToolErrors.ValidationError(path.Length == 0 ? $"Invalid rules: the filter {problem}" : $"Invalid rules at {path}: {problem}");

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.Number => $"the number {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => $"the boolean {value.GetRawText()}",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => "null"
    };
}
