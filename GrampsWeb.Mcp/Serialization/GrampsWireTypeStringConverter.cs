using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes a field that is either a JSON string or a <see cref="GrampsWireTypeObject"/>-shaped object
/// (<c>_class</c>, <c>string</c>, <c>value</c>) into a single <c>string?</c> for models.
/// </summary>
public sealed class GrampsWireTypeStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromWireTypeObject(ref reader),
            _ => throw new JsonException(
                $"Unexpected JSON token for Gramps wire type (string or object with _class/string/value): {reader.TokenType}.")
        };
    }

    private static string? ReadFromWireTypeObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return GrampsWireTypeObject.TryReadPreferredString(doc.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
