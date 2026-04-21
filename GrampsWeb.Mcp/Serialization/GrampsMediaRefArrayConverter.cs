using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes <c>media_list</c> as full <see cref="GrampsMediaRef"/> objects, or bare handle strings.
/// </summary>
public sealed class GrampsMediaRefArrayConverter : JsonConverter<GrampsMediaRef[]?>
{
    public override GrampsMediaRef[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected JSON array for media_list");

        var list = new List<GrampsMediaRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    list.Add(new GrampsMediaRef { Ref = reader.GetString() });
                    break;
                case JsonTokenType.StartObject:
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    var el = doc.RootElement;
                    var m = el.Deserialize<GrampsMediaRef>(options);
                    if (m != null)
                    {
                        // Gramps sometimes uses "handle" for the media id; model maps it to Ref for API/schema alignment.
                        if (string.IsNullOrWhiteSpace(m.Ref)
                            && el.TryGetProperty("handle", out var h)
                            && h.ValueKind == JsonValueKind.String)
                        {
                            m.Ref = h.GetString();
                        }

                        list.Add(m);
                    }

                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, GrampsMediaRef[]? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var m in value)
            JsonSerializer.Serialize(writer, m, options);
        writer.WriteEndArray();
    }
}
