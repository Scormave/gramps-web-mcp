using System.Text.Json;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Case-insensitive JSON property lookup with alias support for custom converters
/// that bypass <see cref="System.Text.Json.JsonSerializerOptions.PropertyNameCaseInsensitive"/>.
/// </summary>
public static class JsonElementPropertyReader
{
    public static string? GetString(JsonElement element, params string[] aliases)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                continue;

            foreach (var alias in aliases)
            {
                if (prop.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }
        }

        return null;
    }

    public static bool GetBool(JsonElement element, params string[] aliases)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                continue;

            foreach (var alias in aliases)
            {
                if (prop.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetBoolean();
            }
        }

        return false;
    }

    public static string[]? GetStringArray(JsonElement element, params string[] aliases)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var alias in aliases)
            {
                if (!prop.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    continue;

                var list = new List<string>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                        list.Add(el.GetString() ?? string.Empty);
                }

                return list.Count > 0 ? list.ToArray() : null;
            }
        }

        return null;
    }

    public static int[]? GetIntArray(JsonElement element, params string[] aliases)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var alias in aliases)
            {
                if (!prop.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    continue;

                var list = new List<int>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                        list.Add(n);
                }

                return list.Count > 0 ? list.ToArray() : null;
            }
        }

        return null;
    }

    public static JsonElement? FindArrayProperty(JsonElement element, params string[] aliases)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var alias in aliases)
            {
                if (prop.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return prop.Value;
            }
        }

        return null;
    }
}
