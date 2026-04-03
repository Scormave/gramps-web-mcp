using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Child refs are usually objects; some API payloads use a plain handle string.
/// </summary>
public sealed class GrampsChildRefJsonConverter : JsonConverter<GrampsChildRef?>
{
    public override GrampsChildRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsChildRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            return new GrampsChildRef
            {
                Ref = GetString(root, "ref"),
                FatherRelType = GetString(root, "frel"),
                MotherRelType = GetString(root, "mrel"),
                Private = root.TryGetProperty("private", out var priv) && priv.ValueKind == JsonValueKind.True,
                TagList = ReadStringArray(root, "tag_list")
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsChildRef)}");
    }

    public override void Write(Utf8JsonWriter writer, GrampsChildRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.FatherRelType))
            writer.WriteString("frel", value.FatherRelType);
        if (!string.IsNullOrEmpty(value.MotherRelType))
            writer.WriteString("mrel", value.MotherRelType);
        if (value.Private)
            writer.WriteBoolean("private", true);
        if (value.TagList is { Length: > 0 })
        {
            writer.WritePropertyName("tag_list");
            writer.WriteStartArray();
            foreach (var t in value.TagList)
                writer.WriteStringValue(t);
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
