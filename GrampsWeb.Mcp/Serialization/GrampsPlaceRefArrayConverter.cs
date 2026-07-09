using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes <c>placeref_list</c> as <see cref="GrampsPlaceRef"/> objects or bare handle strings.
/// </summary>
public sealed class GrampsPlaceRefArrayConverter : JsonConverter<GrampsPlaceRef[]?>
{
    public override GrampsPlaceRef[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected JSON array for placeref_list");

        var list = new List<GrampsPlaceRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    list.Add(new GrampsPlaceRef { Ref = reader.GetString() });
                    break;
                case JsonTokenType.StartObject:
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var item = GrampsPlaceRefJsonConverter.ReadFromElement(doc.RootElement, options);
                    if (item != null)
                        list.Add(item);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, GrampsPlaceRef[]? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}
