using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Shared event-ref object parsing for model and request types.</summary>
internal static class EventRefJsonParsing
{
    internal static (string? Ref, string? Role, string[]? NoteList, object[]? AttributeList) ReadFields(
        JsonElement root,
        JsonSerializerOptions options)
    {
        var noteList = JsonElementPropertyReader.GetStringArray(root, "note_list", "noteList");
        object[]? attributeList = null;
        var attrArray = JsonElementPropertyReader.FindArrayProperty(root, "attribute_list", "attributeList");
        if (attrArray is { } arr)
            attributeList = JsonSerializer.Deserialize<object[]>(arr, options);

        return (
            JsonElementPropertyReader.GetString(root, "ref", "handle"),
            ReadRole(root),
            noteList,
            attributeList);
    }

    private static string? ReadRole(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (!prop.Name.Equals("role", StringComparison.OrdinalIgnoreCase))
                continue;

            return prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Object => GrampsWireTypeObject.TryReadPreferredString(prop.Value),
                _ => null
            };
        }

        return null;
    }
}

/// <summary>
/// Event refs accept Gramps snake_case and common AI camelCase aliases on read.
/// </summary>
public sealed class GrampsEventRefJsonConverter : JsonConverter<GrampsEventRef?>
{
    public override GrampsEventRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsEventRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var (refVal, role, noteList, attributeList) = EventRefJsonParsing.ReadFields(doc.RootElement, options);
            return new GrampsEventRef
            {
                Ref = refVal,
                Role = role,
                NoteList = noteList,
                AttributeList = attributeList
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsEventRef)}");
    }

    public override void Write(Utf8JsonWriter writer, GrampsEventRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.Role))
            writer.WriteString("role", value.Role);
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

        writer.WriteEndObject();
    }
}

/// <summary>
/// Event ref request bodies accept the same aliases as <see cref="GrampsEventRefJsonConverter"/>.
/// </summary>
public sealed class EventRefRequestJsonConverter : JsonConverter<EventRefRequest?>
{
    public override EventRefRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new EventRefRequest { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var (refVal, role, noteList, attributeList) = EventRefJsonParsing.ReadFields(doc.RootElement, options);
            return new EventRefRequest
            {
                Ref = refVal,
                Role = role,
                NoteList = noteList,
                AttributeList = attributeList
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(EventRefRequest)}");
    }

    public override void Write(Utf8JsonWriter writer, EventRefRequest? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.Role))
            writer.WriteString("role", value.Role);
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

        writer.WriteEndObject();
    }
}
