using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleRepositoryRefListJsonConverter : JsonConverter<FlexibleRepositoryRefList?>
{
    private static readonly char[] LineDelimiters = ['|', '\n', '\r'];

    public override FlexibleRepositoryRefList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for repository ref list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleRepositoryRefList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexibleRepositoryRefList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<GrampsRepositoryRef>();
        foreach (var seg in SplitSegments(s))
        {
            var rr = ParseRepositoryRefLine(seg);
            if (rr != null)
                list.Add(rr);
        }

        return new FlexibleRepositoryRefList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitSegments(string s) =>
        s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));

    internal static GrampsRepositoryRef? ParseRepositoryRefLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var parts = line.Split(':', 3, StringSplitOptions.None);
        var @ref = parts[0].Trim();
        if (string.IsNullOrEmpty(@ref))
            throw new JsonException($"Repository ref handle cannot be empty. Line: \"{line}\"");

        var callNumber = parts.Length > 1 ? parts[1].Trim() : null;
        var mediaType = parts.Length > 2 ? parts[2].Trim() : null;

        return new GrampsRepositoryRef
        {
            Ref = @ref,
            CallNumber = string.IsNullOrEmpty(callNumber) ? null : callNumber,
            MediaType = string.IsNullOrEmpty(mediaType) ? null : mediaType
        };
    }

    private static FlexibleRepositoryRefList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsRepositoryRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseRepositoryRefLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var rr = JsonSerializer.Deserialize<GrampsRepositoryRef>(ref reader, options);
                    if (rr != null)
                        list.Add(rr);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexibleRepositoryRefList { Items = list.ToArray() };
    }

    private static FlexibleRepositoryRefList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsRepositoryRef>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(ParseRepositoryRefLine(seg)!);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var rr = el.Deserialize<GrampsRepositoryRef>(options);
                if (rr != null)
                    list.Add(rr);
            }
        }

        return new FlexibleRepositoryRefList { Items = list.ToArray() };
    }
}
