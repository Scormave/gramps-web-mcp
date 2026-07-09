using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads <c>Place.name</c> or <c>PlaceName</c> as plain string or <c>{value, lang, date}</c> object.
/// </summary>
public sealed class GrampsPlaceNameJsonConverter : JsonConverter<GrampsPlaceName?>
{
    public override GrampsPlaceName? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsPlaceName { Value = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ReadFromElement(doc.RootElement, options);
        }

        throw new JsonException($"Unexpected JSON token for place name: {reader.TokenType}.");
    }

    internal static GrampsPlaceName? ReadFromElement(JsonElement root, JsonSerializerOptions options)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        GrampsDate? date = null;
        if (root.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Object)
            date = dateEl.Deserialize<GrampsDate>(options);

        return new GrampsPlaceName
        {
            Value = JsonElementPropertyReader.GetString(root, "value"),
            Lang = JsonElementPropertyReader.GetString(root, "lang"),
            Date = date
        };
    }

    public override void Write(Utf8JsonWriter writer, GrampsPlaceName? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Value))
            writer.WriteString("value", value.Value);
        if (!string.IsNullOrWhiteSpace(value.Lang))
            writer.WriteString("lang", value.Lang);
        if (value.Date != null)
        {
            writer.WritePropertyName("date");
            JsonSerializer.Serialize(writer, value.Date, options);
        }

        writer.WriteEndObject();
    }
}
