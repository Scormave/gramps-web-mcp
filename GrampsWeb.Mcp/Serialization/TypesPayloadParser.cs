using System.Text.Json;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Parses <c>/api/types/default/</c> and <c>/api/types/custom/</c> payloads.
/// Wire format per apispec: each category is a JSON array of strings; some servers may return a string→string map instead.
/// </summary>
public static class TypesPayloadParser
{
    /// <summary>
    /// Maps each top-level property name to a flat list of type labels for display.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string>> ParseCategories(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
            dict[prop.Name] = ParseCategoryValue(prop.Value);
        return dict;
    }

    private static IReadOnlyList<string> ParseCategoryValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                return value.EnumerateArray().Select(ElementToDisplayString).Where(s => s.Length > 0).ToList();
            case JsonValueKind.Object:
                // e.g. DefaultTypeMap-style { "0": "Custom", "1": "Audio" } or arbitrary key→label maps
                return value.EnumerateObject()
                    .OrderBy(p => int.TryParse(p.Name, out var n) ? n : int.MaxValue)
                    .Select(p => ElementToDisplayString(p.Value))
                    .Where(s => s.Length > 0)
                    .ToList();
            case JsonValueKind.String:
            {
                var s = value.GetString();
                return string.IsNullOrEmpty(s) ? Array.Empty<string>() : new[] { s };
            }
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return Array.Empty<string>();
            default:
                return new[] { value.GetRawText() };
        }
    }

    private static string ElementToDisplayString(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => e.GetString() ?? "",
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => e.GetRawText()
        };
}
