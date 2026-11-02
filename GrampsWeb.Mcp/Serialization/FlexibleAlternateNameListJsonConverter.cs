using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleAlternateNameListJsonConverter : JsonConverter<FlexibleAlternateNameList?>
{
    public override FlexibleAlternateNameList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                throw new JsonInputException(
                    $"Unexpected token {reader.TokenType} for alternate name list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleAlternateNameList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleAlternateNameList? ParseFromString(string? s, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();
        if (s.Length > 0 && s[0] == '[')
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(s);
            }
            catch (JsonException ex)
            {
                throw new JsonInputException($"Invalid JSON for alternate name list: {ex.Message}", ex);
            }

            using (doc)
                return ReadFromJsonArray(doc.RootElement, options);
        }

        var lines = s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<GrampsName>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            list.Add(FlexibleGrampsNameParsing.ParseNameLineOrJsonObject(line, options));
        }

        return new FlexibleAlternateNameList { Items = list.ToArray() };
    }

    private static FlexibleAlternateNameList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsName>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(FlexibleGrampsNameParsing.ParseNameLineOrJsonObject(seg.Trim(), options));
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    using var doc = JsonDocument.ParseValue(ref reader);
                    list.Add(FlexibleGrampsNameParsing.ParseObjectElement(doc.RootElement, options));
                    break;
                }
                default:
                    throw new JsonInputException(
                        $"Unexpected token {reader.TokenType} in alternate name list; each entry is a name string or a name object.");
            }
        }

        return new FlexibleAlternateNameList { Items = list.ToArray() };
    }

    private static FlexibleAlternateNameList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsName>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(FlexibleGrampsNameParsing.ParseNameLineOrJsonObject(seg.Trim(), options));
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                list.Add(FlexibleGrampsNameParsing.ParseObjectElement(el, options));
            }
            else
            {
                throw new JsonInputException(
                    $"Unexpected {el.ValueKind} in alternate name list; each entry is a name string or a name object.");
            }
        }

        return new FlexibleAlternateNameList { Items = list.ToArray() };
    }
}
