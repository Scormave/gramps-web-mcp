using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexiblePlaceRefListJsonConverter : JsonConverter<FlexiblePlaceRefList?>
{
    private static readonly char[] Delimiters = [',', '|', '\n', '\r'];

    public override FlexiblePlaceRefList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for place reference list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexiblePlaceRefList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexiblePlaceRefList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<PlaceRefRequest>();
        foreach (var segment in SplitSegments(s))
        {
            var item = ParsePlaceRefLine(segment);
            if (item != null)
                list.Add(item);
        }

        return new FlexiblePlaceRefList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitSegments(string s) =>
        s.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part));

    internal static PlaceRefRequest? ParsePlaceRefLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf("::", StringComparison.Ordinal);
        var href = idx < 0 ? line : line[..idx];
        var dateText = idx < 0 ? null : line[(idx + 2)..];
        href = href.Trim();
        dateText = dateText?.Trim();

        if (string.IsNullOrEmpty(href))
            throw new JsonException($"Place handle cannot be empty. Line: \"{line}\"");

        return new PlaceRefRequest
        {
            Ref = href,
            Date = string.IsNullOrWhiteSpace(dateText)
                ? null
                : AgentDateParser.ToDateRequestOrNull(dateText, DateComponentOrder.Iso)
        };
    }

    private static FlexiblePlaceRefList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<PlaceRefRequest>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParsePlaceRefLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var item = ReadPlaceRefObject(ref reader, options);
                    if (item != null)
                        list.Add(item);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexiblePlaceRefList { Items = list.ToArray() };
    }

    private static FlexiblePlaceRefList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<PlaceRefRequest>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var segment = element.GetString();
                if (string.IsNullOrWhiteSpace(segment))
                    continue;
                list.Add(ParsePlaceRefLine(segment)!);
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var item = ReadPlaceRefFromElement(element, options);
                if (item != null)
                    list.Add(item);
            }
        }

        return new FlexiblePlaceRefList { Items = list.ToArray() };
    }

    private static PlaceRefRequest? ReadPlaceRefObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadPlaceRefFromElement(doc.RootElement, options);
    }

    internal static PlaceRefRequest? ReadPlaceRefFromElement(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var href = HandleElementReader.ReadHandleFromElement(element);
        if (string.IsNullOrWhiteSpace(href))
            return null;

        DateRequest? date = null;
        if (element.TryGetProperty("date", out var dateEl))
        {
            if (dateEl.ValueKind == JsonValueKind.String)
                date = AgentDateParser.ToDateRequestOrNull(dateEl.GetString(), DateComponentOrder.Iso);
            else if (dateEl.ValueKind == JsonValueKind.Object)
                date = dateEl.Deserialize<DateRequest>(options);
        }

        return new PlaceRefRequest { Ref = href, Date = date };
    }
}
