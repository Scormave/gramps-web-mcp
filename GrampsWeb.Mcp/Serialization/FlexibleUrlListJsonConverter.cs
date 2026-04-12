using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleUrlListJsonConverter : JsonConverter<FlexibleUrlList?>
{
    private static readonly char[] LineDelimiters = ['|', '\n', '\r'];

    public override FlexibleUrlList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for URL list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleUrlList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleUrlList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<GrampsUrl>();
        foreach (var seg in SplitSegments(s))
        {
            var u = ParseUrlLine(seg);
            if (u != null)
                list.Add(u);
        }

        return new FlexibleUrlList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitSegments(string s) =>
        s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));

    /// <summary>First <c>:</c> separates type from path; path may be followed by <c> — </c> or <c> - </c> and description.</summary>
    internal static GrampsUrl? ParseUrlLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf(':');
        if (idx < 0)
            throw new JsonException($"URL entry must use \"Type: URL\" (colon). Got: \"{line}\"");

        var type = line[..idx].Trim();
        var rest = line[(idx + 1)..].Trim();
        if (string.IsNullOrEmpty(type))
            throw new JsonException($"URL type cannot be empty. Line: \"{line}\"");

        string path;
        string? desc = null;

        var em = rest.IndexOf(" — ", StringComparison.Ordinal);
        if (em >= 0)
        {
            path = rest[..em].Trim();
            desc = rest[(em + 3)..].Trim();
        }
        else
        {
            var asc = rest.IndexOf(" - ", StringComparison.Ordinal);
            if (asc >= 0)
            {
                path = rest[..asc].Trim();
                desc = rest[(asc + 3)..].Trim();
            }
            else
                path = rest;
        }

        if (string.IsNullOrEmpty(path))
            throw new JsonException($"URL path cannot be empty. Line: \"{line}\"");

        return new GrampsUrl { Type = type, Path = path, Description = string.IsNullOrEmpty(desc) ? null : desc };
    }

    private static FlexibleUrlList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsUrl>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseUrlLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var u = JsonSerializer.Deserialize<GrampsUrl>(ref reader, options);
                    if (u != null)
                        list.Add(u);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexibleUrlList { Items = list.ToArray() };
    }

    private static FlexibleUrlList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsUrl>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(ParseUrlLine(seg)!);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var u = el.Deserialize<GrampsUrl>(options);
                if (u != null)
                    list.Add(u);
            }
        }

        return new FlexibleUrlList { Items = list.ToArray() };
    }
}
