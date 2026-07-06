using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Repository refs are normally objects; some servers return a plain repository handle string.
/// </summary>
public sealed class GrampsRepositoryRefJsonConverter : JsonConverter<GrampsRepositoryRef?>
{
    public override GrampsRepositoryRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsRepositoryRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new GrampsRepositoryRef
            {
                Ref = JsonElementPropertyReader.GetString(root, "ref", "handle"),
                CallNumber = JsonElementPropertyReader.GetString(root, "call_number", "callNumber"),
                MediaType = JsonElementPropertyReader.GetString(root, "media_type", "mediaType"),
                NoteList = JsonElementPropertyReader.GetStringArray(root, "note_list", "noteList"),
                Private = JsonElementPropertyReader.GetBool(root, "private")
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsRepositoryRef)}");
    }

    public override void Write(Utf8JsonWriter writer, GrampsRepositoryRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.CallNumber))
            writer.WriteString("call_number", value.CallNumber);
        if (!string.IsNullOrEmpty(value.MediaType))
            writer.WriteString("media_type", value.MediaType);
        if (value.Private)
            writer.WriteBoolean("private", true);
        if (value.NoteList is { Length: > 0 })
        {
            writer.WritePropertyName("note_list");
            writer.WriteStartArray();
            foreach (var n in value.NoteList)
                writer.WriteStringValue(n);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
