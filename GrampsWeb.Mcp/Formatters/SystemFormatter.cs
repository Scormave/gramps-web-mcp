using System.Text;
using System.Text.Json;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Formats system-level JSON API responses (metadata, transactions, bookmarks).
/// </summary>
public static class SystemFormatter
{
    private static readonly System.Globalization.TextInfo TextInfo =
        System.Globalization.CultureInfo.CurrentCulture.TextInfo;

    /// <param name="defaultPersonFullName">
    /// When set and <c>default_person</c> in metadata is a string handle, output includes this display name
    /// plus the handle on a separate line instead of only the handle.
    /// </param>
    public static string FormatMetadata(JsonElement metadata, string? defaultPersonFullName = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DATABASE METADATA");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        try
        {
            foreach (var property in metadata.EnumerateObject())
            {
                if (property.Name == "default_person"
                    && defaultPersonFullName != null
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    var handle = property.Value.GetString() ?? "";
                    sb.AppendLine(ToDisplayLabel(property.Name));
                    var indent = new string(' ', 2);
                    sb.AppendLine($"{indent}{"Name",-28} {defaultPersonFullName}");
                    sb.AppendLine($"{indent}{"Handle",-28} {handle}");
                    continue;
                }

                var label = ToDisplayLabel(property.Name);
                AppendMetadataProperty(sb, label, property.Value);
            }
        }
        catch { }

        return sb.ToString();
    }

    private static string ToDisplayLabel(string key) => TextInfo.ToTitleCase(key.Replace('_', ' '));

    private static void AppendMetadataProperty(StringBuilder sb, string label, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine(label);
                foreach (var child in value.EnumerateObject())
                {
                    AppendIndentedValue(sb, 1, ToDisplayLabel(child.Name), child.Value);
                }
                break;
            case JsonValueKind.Array:
                sb.AppendLine(label);
                AppendArray(sb, value, 1);
                break;
            default:
                sb.AppendLine($"{label,-30} {GetScalarValue(value)}");
                break;
        }
    }

    private static void AppendIndentedValue(StringBuilder sb, int indentLevel, string label, JsonElement value)
    {
        var indent = new string(' ', indentLevel * 2);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                sb.AppendLine($"{indent}{label}:");
                foreach (var child in value.EnumerateObject())
                {
                    AppendIndentedValue(sb, indentLevel + 1, ToDisplayLabel(child.Name), child.Value);
                }
                break;
            case JsonValueKind.Array:
                sb.AppendLine($"{indent}{label}:");
                AppendArray(sb, value, indentLevel + 1);
                break;
            default:
                sb.AppendLine($"{indent}{label,-28} {GetScalarValue(value)}");
                break;
        }
    }

    private static void AppendArray(StringBuilder sb, JsonElement value, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);
        int idx = 1;
        foreach (var item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.Object:
                    sb.AppendLine($"{indent}-");
                    foreach (var child in item.EnumerateObject())
                    {
                        AppendIndentedValue(sb, indentLevel + 1, ToDisplayLabel(child.Name), child.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    sb.AppendLine($"{indent}{idx}.");
                    AppendArray(sb, item, indentLevel + 1);
                    idx++;
                    break;
                default:
                    sb.AppendLine($"{indent}- {GetScalarValue(item)}");
                    break;
            }
        }
    }

    private static string GetScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.ToString()
    };

    public static string FormatRecentChanges(JsonElement changes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("RECENT CHANGES");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        try
        {
            if (changes.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var item in changes.EnumerateArray())
                {
                    count++;
                    var fields = new List<string>();
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            fields.Add($"{prop.Name}: {prop.Value.GetString()}");
                        else if (prop.Value.ValueKind == JsonValueKind.Number)
                            fields.Add($"{prop.Name}: {prop.Value}");
                    }
                    if (fields.Count > 0)
                        sb.AppendLine($"{count}. {string.Join(" | ", fields)}");
                }
                if (count == 0)
                    sb.AppendLine("No recent changes found.");
            }
            else
            {
                sb.AppendLine(JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }

        return sb.ToString();
    }

    public static string FormatBookmarks(JsonElement bookmarks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("USER BOOKMARKS");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        try
        {
            if (bookmarks.ValueKind == JsonValueKind.Array)
            {
                var items = bookmarks.EnumerateArray().ToList();
                if (items.Count == 0)
                {
                    sb.AppendLine("No bookmarks found.");
                }
                else
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var bm = items[i];
                        var handle = "";
                        var objectType = "";
                        var name = "";

                        foreach (var prop in bm.EnumerateObject())
                        {
                            if (prop.Name == "handle" && prop.Value.ValueKind == JsonValueKind.String)
                                handle = prop.Value.GetString() ?? "";
                            if (prop.Name == "object_type" && prop.Value.ValueKind == JsonValueKind.String)
                                objectType = prop.Value.GetString() ?? "";
                            if (prop.Name == "name" && prop.Value.ValueKind == JsonValueKind.String)
                                name = prop.Value.GetString() ?? "";
                        }

                        sb.AppendLine($"{i + 1}. [{objectType}] {name}");
                        sb.AppendLine($"   Handle: {handle}");
                    }
                }
            }
            else
            {
                sb.AppendLine(JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }

        return sb.ToString();
    }
}
