using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes text-like MCP arguments from JSON strings or numbers.
/// </summary>
public sealed class FlexibleStringJsonConverter : JsonConverter<FlexibleString?>
{
    public override FlexibleString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => new FlexibleString { Value = reader.GetString() ?? string.Empty },
            JsonTokenType.Number => new FlexibleString { Value = ReadRawTokenText(ref reader) },
            _ => throw new JsonException(
                $"Unexpected token {reader.TokenType} for text value; use JSON string, number, or null.")
        };
    }

    public override void Write(Utf8JsonWriter writer, FlexibleString? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value);
    }

    private static string ReadRawTokenText(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence)
            return Encoding.UTF8.GetString(reader.ValueSpan);

        return Encoding.UTF8.GetString(reader.ValueSequence.ToArray());
    }
}
