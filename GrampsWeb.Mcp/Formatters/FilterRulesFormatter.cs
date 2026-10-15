using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Text of the Gramps filter rules that Gramps Web offers (<c>/api/filters/</c>), for the rules argument of
/// list_objects. Gramps Web API 3.23 describes each value of a rule; older versions give only the labels.
/// </summary>
internal static class FilterRulesFormatter
{
    private const int SeparatorWidth = 60;

    /// <summary>The filter shape, the bridges of nested filters and the number of rules and saved filters per type.</summary>
    public static string FormatOverview(JsonElement catalogs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FILTER RULES");
        sb.AppendLine(new string('=', SeparatorWidth));
        sb.AppendLine();
        sb.AppendLine("list_objects selects records with Gramps filter rules in its rules argument:");
        sb.AppendLine("  {\"function\": \"and\", \"invert\": false, \"rules\": [{\"name\": \"IsAncestorOf\", \"values\": [\"I0001\", true]}]}");
        sb.AppendLine("function is and (the default), or, or one (exactly one item matches); invert: true keeps the records that do not match.");
        sb.AppendLine("A rule takes one value per label, in order; regex: true makes its text values regular expressions.");
        sb.AppendLine("An item of rules can be a nested filter of the same form. With \"namespace\" it matches the records");
        sb.AppendLine("each listed one links to: from people Event or Family, from families Event or Person, from events Place,");
        sb.AppendLine("from citations Source, from sources Repository. People with a Residence event:");
        sb.AppendLine("  {\"rules\": [{\"namespace\": \"Event\", \"rules\": [{\"name\": \"HasType\", \"values\": [\"Residence\"]}]}]}");
        sb.AppendLine("A saved filter of the tree is the rule MatchesFilter with the filter's name.");
        sb.AppendLine();
        sb.AppendLine("Rules by type (get_reference topic filter-rules with the type as section lists them and their values):");
        foreach (var objectType in FilterRules.Namespaces.Keys)
        {
            if (catalogs.ValueKind != JsonValueKind.Object || !catalogs.TryGetProperty(objectType, out var catalog))
                continue;
            var rules = Rules(catalog).Count();
            var saved = SavedFilters(catalog).Select(Text("name")).Where(name => name.Length > 0).ToList();
            sb.Append($"- {objectType}: {rules} rules");
            sb.AppendLine(saved.Count == 0 ? "" : $"; saved filters: {string.Join(", ", saved)}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The rules of one type by category, each with its values and description, then the saved filters.</summary>
    public static string FormatType(string objectType, JsonElement catalog)
    {
        var rules = Rules(catalog).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"FILTER RULES: {objectType} ({rules.Count})");
        sb.AppendLine(new string('=', SeparatorWidth));
        sb.AppendLine();
        sb.AppendLine(rules.Any(rule => rule.TryGetProperty("types", out _))
            ? "Values follow the labels, one per label, in order; a label without a kind takes text. " +
              "Type names (event type and others) as get_reference topic types lists them."
            : "Values follow the labels, one per label, in order. This Gramps Web version does not describe them: " +
              "ID labels take a Gramps ID such as I0001, Inclusive and Case sensitive take true or false, Number of … labels " +
              "take a number, Number must be is less than, equal to or greater than, Confidence level is 0 (very low) to " +
              "4 (very high), Date takes a date and Filter name a saved filter name; the others take text.");

        foreach (var category in rules
                     .GroupBy(Text("category"))
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine(category.Key.Length == 0 ? "Other" : category.Key);
            foreach (var rule in category.OrderBy(Text("rule"), StringComparer.Ordinal))
            {
                var parameters = Parameters(rule);
                var description = string.Join(' ', Text("description")(rule).Split(' ', StringSplitOptions.RemoveEmptyEntries));
                sb.Append($"- {Text("rule")(rule)}");
                if (parameters.Count > 0)
                    sb.Append($" ({string.Join("; ", parameters)})");
                sb.AppendLine(description.Length == 0 ? "" : $" — {description}");
            }
        }

        sb.AppendLine();
        var saved = SavedFilters(catalog).ToList();
        if (saved.Count == 0)
        {
            sb.AppendLine($"No saved {objectType} filters.");
        }
        else
        {
            sb.AppendLine("SAVED FILTERS (use with MatchesFilter)");
            foreach (var filter in saved)
                sb.AppendLine($"- {DescribeSavedFilter(filter)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<JsonElement> Rules(JsonElement catalog) => Items(catalog, "rules");

    private static IEnumerable<JsonElement> SavedFilters(JsonElement catalog) => Items(catalog, "filters");

    private static IEnumerable<JsonElement> Items(JsonElement catalog, string name) =>
        catalog.ValueKind == JsonValueKind.Object
        && catalog.TryGetProperty(name, out var items)
        && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static Func<JsonElement, string> Text(string name) => element =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : "";

    /// <summary>"ID: person ID", "Inclusive: true/false", "Place" — each label with the kind of its value.</summary>
    private static List<string> Parameters(JsonElement rule)
    {
        var labels = rule.TryGetProperty("labels", out var labelList) && labelList.ValueKind == JsonValueKind.Array
            ? labelList.EnumerateArray().Select(label => label.ValueKind == JsonValueKind.String ? label.GetString()! : "").ToList()
            : [];
        var types = rule.TryGetProperty("types", out var typeList) && typeList.ValueKind == JsonValueKind.Array
            ? typeList.EnumerateArray().ToList()
            : [];

        return labels.Select((label, index) =>
        {
            var name = label.Trim().TrimEnd(':').Trim();
            var kind = index < types.Count ? DescribeKind(types[index]) : null;
            return kind == null ? name : name.Length == 0 ? kind : $"{name}: {kind}";
        }).ToList();
    }

    /// <summary>The kind of value a Gramps Web 3.23 type describes; null for plain text.</summary>
    private static string? DescribeKind(JsonElement type)
    {
        var kind = Text("type")(type);
        var ns = Text("namespace")(type).ToLowerInvariant();
        return kind switch
        {
            "text" or "" => null,
            "id" => ns.Length == 0 ? "Gramps ID" : $"{ns} ID",
            "boolean" => "true/false",
            "integer" => (Number(type, "min"), Number(type, "max")) switch
            {
                ({ } min, { } max) => $"number {min}–{max}",
                ({ } min, null) => $"number from {min}",
                (null, { } max) => $"number up to {max}",
                _ => "number"
            },
            "select" => Options(type),
            "gramps_type" => Text("default_types")(type) is { Length: > 0 } names
                ? names.Replace("_types", " type").Replace('_', ' ')
                : "type name",
            "date" => "date",
            "datetime" => "date and time",
            "tag" => "tag name",
            "goql" => "GOQL expression",
            "filter" => ns.Length == 0 ? "saved filter name" : $"saved {ns} filter name",
            _ => kind
        };
    }

    private static string? Number(JsonElement type, string name) =>
        type.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;

    /// <summary>"less than | equal to | greater than", or "0 (Very Low) | 1 (Low) | …" when labels differ from values.</summary>
    private static string Options(JsonElement type)
    {
        var options = type.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(option => (value: Text("value")(option), label: Text("label")(option)))
                .Where(option => option.value.Length > 0)
                .Select(option => option.label.Length == 0 || option.label == option.value
                    ? option.value
                    : $"{option.value} ({option.label})")
                .ToList()
            : [];
        return options.Count == 0 ? "one of a list" : string.Join(" | ", options);
    }

    /// <summary>"Ancestors (or, inverted): IsAncestorOf [I0001, 1]; IsMale — comment".</summary>
    private static string DescribeSavedFilter(JsonElement filter)
    {
        var rules = Items(filter, "rules").Select(rule =>
        {
            var values = rule.TryGetProperty("values", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()).ToList()
                : [];
            var regex = rule.TryGetProperty("regex", out var flag) && flag.ValueKind == JsonValueKind.True;
            return Text("name")(rule)
                   + (values.Count == 0 ? "" : $" [{string.Join(", ", values)}]")
                   + (regex ? " (regex)" : "");
        }).ToList();

        var function = Text("function")(filter);
        var inverted = filter.TryGetProperty("invert", out var invert) && invert.ValueKind == JsonValueKind.True;
        var how = new List<string>();
        if (function.Length > 0 && function != "and" && rules.Count > 1)
            how.Add(function);
        if (inverted)
            how.Add("inverted");

        var comment = Text("comment")(filter);
        return $"{Text("name")(filter)}{(how.Count == 0 ? "" : $" ({string.Join(", ", how)})")}: {string.Join("; ", rules)}"
               + (comment.Length == 0 ? "" : $" — {comment}");
    }
}
