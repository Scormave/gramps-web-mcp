using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexiblePlaceNameListJsonConverter : JsonConverter<FlexiblePlaceNameList?>
{
    private static readonly char[] LineDelimiters = ['\n', '\r'];

    public override FlexiblePlaceNameList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for place name list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexiblePlaceNameList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexiblePlaceNameList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<PlaceNameRequest>();
        foreach (var line in s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var item = ParsePlaceNameLine(line, options);
            if (item != null)
                list.Add(item);
        }

        return new FlexiblePlaceNameList { Items = list.ToArray() };
    }

    internal static PlaceNameRequest? ParsePlaceNameLine(string line, JsonSerializerOptions options)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf("::", StringComparison.Ordinal);
        var value = idx < 0 ? line : line[..idx];
        var lang = idx < 0 ? null : line[(idx + 2)..];
        value = value.Trim();
        lang = lang?.Trim();

        if (string.IsNullOrEmpty(value))
            throw new JsonException($"Place name value cannot be empty. Line: \"{line}\"");

        return new PlaceNameRequest
        {
            Value = value,
            Lang = string.IsNullOrWhiteSpace(lang) ? null : lang
        };
    }

    private static FlexiblePlaceNameList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<PlaceNameRequest>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParsePlaceNameLine(seg, options)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var item = ReadPlaceNameObject(ref reader, options);
                    if (item != null)
                        list.Add(item);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexiblePlaceNameList { Items = list.ToArray() };
    }

    private static FlexiblePlaceNameList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<PlaceNameRequest>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var segment = element.GetString();
                if (string.IsNullOrWhiteSpace(segment))
                    continue;
                list.Add(ParsePlaceNameLine(segment, options)!);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var item = ReadPlaceNameFromElement(element, options);
                if (item != null)
                    list.Add(item);
            }
        }

        return new FlexiblePlaceNameList { Items = list.ToArray() };
    }

    private static PlaceNameRequest? ReadPlaceNameObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadPlaceNameFromElement(doc.RootElement, options);
    }

    internal static PlaceNameRequest? ReadPlaceNameFromElement(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var value = JsonElementPropertyReader.GetString(element, "value");
        if (string.IsNullOrWhiteSpace(value))
            return null;

        DateRequest? date = null;
        if (element.TryGetProperty("date", out var dateEl))
        {
            if (dateEl.ValueKind == JsonValueKind.String)
                date = AgentDateParser.ToDateRequestOrNull(dateEl.GetString(), DateComponentOrder.Iso);
            else if (dateEl.ValueKind == JsonValueKind.Object)
                date = dateEl.Deserialize<DateRequest>(options);
        }

        return new PlaceNameRequest
        {
            Value = value.Trim(),
            Lang = JsonElementPropertyReader.GetString(element, "lang"),
            Date = date
        };
    }
}
