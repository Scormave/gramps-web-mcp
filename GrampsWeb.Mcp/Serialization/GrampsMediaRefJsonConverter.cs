using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Media refs accept Gramps snake_case and common AI camelCase aliases on read.
/// </summary>
public sealed class GrampsMediaRefJsonConverter : JsonConverter<GrampsMediaRef?>
{
    public override GrampsMediaRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsMediaRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            object[]? attributeList = null;
            var attrArray = JsonElementPropertyReader.FindArrayProperty(root, "attribute_list", "attributeList");
            if (attrArray is { } arr)
                attributeList = JsonSerializer.Deserialize<object[]>(arr, options);

            return new GrampsMediaRef
            {
                Ref = JsonElementPropertyReader.GetString(root, "ref", "handle"),
                Private = JsonElementPropertyReader.GetBool(root, "private"),
                CitationList = JsonElementPropertyReader.GetStringArray(root, "citation_list", "citationList"),
                NoteList = JsonElementPropertyReader.GetStringArray(root, "note_list", "noteList"),
                AttributeList = attributeList,
                Rect = JsonElementPropertyReader.GetIntArray(root, "rect")
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsMediaRef)}");
    }

    public override void Write(Utf8JsonWriter writer, GrampsMediaRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (value.Private)
            writer.WriteBoolean("private", true);
        if (value.CitationList is { Length: > 0 })
        {
            writer.WritePropertyName("citation_list");
            writer.WriteStartArray();
            foreach (var c in value.CitationList)
                writer.WriteStringValue(c);
            writer.WriteEndArray();
        }

        if (value.NoteList is { Length: > 0 })
        {
            writer.WritePropertyName("note_list");
            writer.WriteStartArray();
            foreach (var n in value.NoteList)
                writer.WriteStringValue(n);
            writer.WriteEndArray();
        }

        if (value.AttributeList is { Length: > 0 })
        {
            writer.WritePropertyName("attribute_list");
            JsonSerializer.Serialize(writer, value.AttributeList, options);
        }

        if (value.Rect is { Length: > 0 })
        {
            writer.WritePropertyName("rect");
            writer.WriteStartArray();
            foreach (var r in value.Rect)
                writer.WriteNumberValue(r);
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}
