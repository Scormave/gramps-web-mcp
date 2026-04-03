using System.Text;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats Gramps type vocabulary API responses.
/// </summary>
public static class TypesFormatter
{
    public static string FormatTypesResponse(IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Built-in Gramps Type Vocabularies:");
        sb.AppendLine("=====================================");

        foreach (var kv in categories.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            AddTypeSection(sb, HumanizeCategoryKey(kv.Key), kv.Value);

        return sb.ToString();
    }

    public static string FormatCustomTypesResponse(IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
    {
        if (categories == null || categories.Count == 0)
            return "No custom types defined in this database.";

        var sb = new StringBuilder();
        sb.AppendLine("Custom Type Vocabularies:");
        sb.AppendLine("=========================");

        foreach (var kv in categories.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            AddTypeSection(sb, HumanizeCategoryKey(kv.Key), kv.Value);

        return sb.ToString();
    }

    private static void AddTypeSection(StringBuilder sb, string sectionName, IReadOnlyList<string>? values)
    {
        if (values == null || values.Count == 0)
        {
            sb.AppendLine($"\n{sectionName}: (none)");
            return;
        }

        sb.AppendLine($"\n{sectionName}:");
        sb.AppendLine(new string('-', sectionName.Length + 1));

        foreach (var v in values)
            sb.AppendLine($"  • {v}");
    }

    /// <summary>Turns <c>event_types</c> into <c>Event types</c> (sentence case).</summary>
    internal static string HumanizeCategoryKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return key;
        var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return key;
        var phrase = string.Join(" ", parts.Select(static p => p.ToLowerInvariant()));
        return char.ToUpperInvariant(phrase[0]) + phrase[1..];
    }
}
