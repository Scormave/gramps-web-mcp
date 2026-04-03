using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Gramps Web returns <c>Place.name</c> as either a plain string or a <c>PlaceName</c> object
/// (<c>{"value":"Twin Falls","lang":"..."}</c> per apispec). MCP keeps a single display string on <see cref="Models.GrampsPlace"/>.
/// </summary>
public sealed class GrampsPlaceNameStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromPlaceNameObject(ref reader),
            _ => throw new JsonException($"Unexpected JSON token for place name: {reader.TokenType}.")
        };
    }

    private static string? ReadFromPlaceNameObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;
        if (!doc.RootElement.TryGetProperty("value", out var valueEl))
            return null;
        return valueEl.ValueKind switch
        {
            JsonValueKind.String => valueEl.GetString(),
            JsonValueKind.Null => null,
            _ => valueEl.ToString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
