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
                Ref = GetString(root, "ref"),
                CallNumber = GetString(root, "call_number"),
                MediaType = GetString(root, "media_type"),
                NoteList = ReadStringArray(root, "note_list"),
                Private = root.TryGetProperty("private", out var priv) && priv.ValueKind == JsonValueKind.True
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

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private static string[]? ReadStringArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
                list.Add(el.GetString() ?? string.Empty);
        }

        return list.Count > 0 ? list.ToArray() : null;
    }
}
