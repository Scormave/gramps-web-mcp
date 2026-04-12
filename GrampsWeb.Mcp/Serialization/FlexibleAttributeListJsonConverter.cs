using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Deserializes <see cref="FlexibleAttributeList"/> from JSON array, string, or null.</summary>
public sealed class FlexibleAttributeListJsonConverter : JsonConverter<FlexibleAttributeList?>
{
    private static readonly char[] LineDelimiters = ['|', '\n', '\r'];

    public override FlexibleAttributeList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return ParseFromString(reader.GetString(), options);
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, options);
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} for attribute list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleAttributeList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleAttributeList? ParseFromString(string? s, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();
        if (s.Length > 0 && s[0] == '[')
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;
                return ReadFromJsonArray(doc.RootElement, options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var segments = SplitAttributeSegments(s);
        var list = new List<GrampsAttribute>();
        foreach (var seg in segments)
        {
            var a = ParseTypeValueLine(seg);
            if (a != null)
                list.Add(a);
        }

        return new FlexibleAttributeList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitAttributeSegments(string s)
    {
        var parts = s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p));
    }

    /// <summary>First colon separates type (trimmed) from value (trimmed remainder).</summary>
    internal static GrampsAttribute? ParseTypeValueLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf(':');
        if (idx < 0)
            throw new JsonException($"Attribute entry must use \"Type: Value\" (colon). Got: \"{line}\"");

        var type = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();
        if (string.IsNullOrEmpty(type))
            throw new JsonException($"Attribute type cannot be empty. Line: \"{line}\"");

        return new GrampsAttribute { Type = type, Value = value };
    }

    private static FlexibleAttributeList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsAttribute>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseTypeValueLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var attr = JsonSerializer.Deserialize<GrampsAttribute>(ref reader, options);
                    if (attr != null)
                        list.Add(attr);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexibleAttributeList { Items = list.ToArray() };
    }

    private static FlexibleAttributeList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsAttribute>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(ParseTypeValueLine(seg)!);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var attr = el.Deserialize<GrampsAttribute>(options);
                if (attr != null)
                    list.Add(attr);
            }
        }

        return new FlexibleAttributeList { Items = list.ToArray() };
    }
}
