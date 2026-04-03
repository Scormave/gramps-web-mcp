using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Gramps mutation payloads may return <c>Event.type</c> as an object:
/// <c>{ "_class": "EventType", "string": "", "value": 12 }</c>.
/// This converter supports that object shape.
/// </summary>
public sealed class GrampsEventTypeObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromEventTypeObject(ref reader),
            _ => throw new JsonException($"Unexpected JSON token for event type: {reader.TokenType}.")
        };
    }

    private static string? ReadFromEventTypeObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("string", out var str) && str.ValueKind == JsonValueKind.String)
        {
            var s = str.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        if (root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number)
        {
            return val.GetRawText();
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}
