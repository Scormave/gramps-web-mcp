using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads an array of handles where each element may be a string or an object with <c>ref</c> or <c>handle</c>.
/// </summary>
public sealed class GrampsHandleStringArrayConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected JSON array for handle list");

        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(ReadHandleElement(ref reader));

        return list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var s in value)
            writer.WriteStringValue(s);
        writer.WriteEndArray();
    }

    private static string ReadHandleElement(ref Utf8JsonReader reader)
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
}
