using System.Text.Json;
using System.Text.Json.Serialization;
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
                throw new JsonException(
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

        var lines = s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<GrampsName>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            list.Add(FlexibleGrampsNameParsing.ParseSimpleLine(line));
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
                    list.Add(FlexibleGrampsNameParsing.ParseSimpleLine(seg.Trim()));
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var n = JsonSerializer.Deserialize<GrampsName>(ref reader, options);
                    if (n != null)
                        list.Add(n);
                    break;
                }
                default:
                    reader.Skip();
                    break;
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
                list.Add(FlexibleGrampsNameParsing.ParseSimpleLine(seg.Trim()));
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var n = el.Deserialize<GrampsName>(options);
                if (n != null)
                    list.Add(n);
            }
        }

        return new FlexibleAlternateNameList { Items = list.ToArray() };
    }
}
