using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Person association refs accept Gramps snake_case and common AI camelCase / semantic aliases on read.
/// </summary>
public sealed class GrampsPersonRefJsonConverter : JsonConverter<GrampsPersonRef?>
{
    public override GrampsPersonRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsPersonRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ParseObject(doc.RootElement);
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsPersonRef)}");
    }

    internal static GrampsPersonRef ParseObject(JsonElement root) =>
        new()
        {
            Ref = JsonElementPropertyReader.GetString(root, "ref", "handle"),
            Relationship = JsonElementPropertyReader.GetString(root, "rel", "relationship"),
            CitationList = JsonElementPropertyReader.GetStringArray(root, "citation_list", "citationList"),
            NoteList = JsonElementPropertyReader.GetStringArray(root, "note_list", "noteList"),
            Private = JsonElementPropertyReader.GetBool(root, "private")
        };

    public override void Write(Utf8JsonWriter writer, GrampsPersonRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.Relationship))
            writer.WriteString("rel", value.Relationship);
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

        writer.WriteEndObject();
    }
}
