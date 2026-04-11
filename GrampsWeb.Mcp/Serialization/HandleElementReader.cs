using System.Text.Json;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads a single Gramps handle from JSON: string, or object with <c>ref</c> / <c>handle</c>.
/// </summary>
public static class HandleElementReader
{
    public static string ReadHandleElement(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;
            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var r = doc.RootElement;
                if (r.TryGetProperty("ref", out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString() ?? string.Empty;
                if (r.TryGetProperty("handle", out var h) && h.ValueKind == JsonValueKind.String)
                    return h.GetString() ?? string.Empty;
                return string.Empty;
            }
            default:
                reader.Skip();
                return string.Empty;
        }
    }

    /// <summary>Parses the same shapes as <see cref="ReadHandleElement"/> from a <see cref="JsonElement"/>.</summary>
    public static string ReadHandleFromElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? string.Empty;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("ref", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? string.Empty;
            if (element.TryGetProperty("handle", out var h) && h.ValueKind == JsonValueKind.String)
                return h.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}
