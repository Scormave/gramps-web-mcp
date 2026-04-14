using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleChildRefListJsonConverter : JsonConverter<FlexibleChildRefList?>
{
    private static readonly char[] Delimiters = [',', '|', '\n', '\r'];

    public override FlexibleChildRefList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for child reference list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleChildRefList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleChildRefList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<GrampsChildRef>();
        foreach (var segment in SplitSegments(s))
        {
            var item = ParseChildRefLine(segment);
            if (item != null)
                list.Add(item);
        }

        return new FlexibleChildRefList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitSegments(string s) =>
        s.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part));

    internal static GrampsChildRef? ParseChildRefLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf("::", StringComparison.Ordinal);
        var href = idx < 0 ? line : line[..idx];
        var relType = idx < 0 ? "Birth" : line[(idx + 2)..];
        href = href.Trim();
        relType = relType.Trim();

        if (string.IsNullOrEmpty(href))
            throw new JsonException($"Child handle cannot be empty. Line: \"{line}\"");

        relType = string.IsNullOrEmpty(relType) ? "Birth" : relType;

        return new GrampsChildRef
        {
            Ref = href,
            FatherRelType = relType,
            MotherRelType = relType
        };
    }

    private static FlexibleChildRefList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsChildRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseChildRefLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var item = JsonSerializer.Deserialize<GrampsChildRef>(ref reader, options);
                    if (item != null)
                        list.Add(item);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexibleChildRefList { Items = list.ToArray() };
    }

    private static FlexibleChildRefList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsChildRef>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var segment = element.GetString();
                if (string.IsNullOrWhiteSpace(segment))
                    continue;
                list.Add(ParseChildRefLine(segment)!);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var item = element.Deserialize<GrampsChildRef>(options);
                if (item != null)
                    list.Add(item);
            }
        }

        return new FlexibleChildRefList { Items = list.ToArray() };
    }
}
