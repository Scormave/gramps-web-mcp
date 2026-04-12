using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexiblePersonRefListJsonConverter : JsonConverter<FlexiblePersonRefList?>
{
    private static readonly char[] LineDelimiters = ['|', '\n', '\r'];

    public override FlexiblePersonRefList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    $"Unexpected token {reader.TokenType} for person association list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexiblePersonRefList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Items, options);
    }

    private static FlexiblePersonRefList? ParseFromString(string? s, JsonSerializerOptions options)
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

        var list = new List<GrampsPersonRef>();
        foreach (var seg in SplitSegments(s))
        {
            var p = ParseAssociationLine(seg);
            if (p != null)
                list.Add(p);
        }

        return new FlexiblePersonRefList { Items = list.ToArray() };
    }

    private static IEnumerable<string> SplitSegments(string s) =>
        s.Split(LineDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));

    /// <summary>Split on first <c>::</c>; left is person handle, right is relationship (trimmed).</summary>
    internal static GrampsPersonRef? ParseAssociationLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line))
            return null;

        var idx = line.IndexOf("::", StringComparison.Ordinal);
        if (idx < 0)
            throw new JsonException(
                $"Person association must use \"HANDLE:: relationship\" (double colon). Got: \"{line}\"");

        var href = line[..idx].Trim();
        var rel = line[(idx + 2)..].Trim();
        if (string.IsNullOrEmpty(href))
            throw new JsonException($"Person handle cannot be empty. Line: \"{line}\"");

        return new GrampsPersonRef { Ref = href, Relationship = string.IsNullOrEmpty(rel) ? null : rel };
    }

    private static FlexiblePersonRefList ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<GrampsPersonRef>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    var seg = reader.GetString();
                    if (string.IsNullOrWhiteSpace(seg))
                        break;
                    list.Add(ParseAssociationLine(seg)!);
                    break;
                }
                case JsonTokenType.StartObject:
                {
                    var p = JsonSerializer.Deserialize<GrampsPersonRef>(ref reader, options);
                    if (p != null)
                        list.Add(p);
                    break;
                }
                default:
                    reader.Skip();
                    break;
            }
        }

        return new FlexiblePersonRefList { Items = list.ToArray() };
    }

    private static FlexiblePersonRefList ReadFromJsonArray(JsonElement array, JsonSerializerOptions options)
    {
        var list = new List<GrampsPersonRef>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var seg = el.GetString();
                if (string.IsNullOrWhiteSpace(seg))
                    continue;
                list.Add(ParseAssociationLine(seg)!);
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var p = el.Deserialize<GrampsPersonRef>(options);
                if (p != null)
                    list.Add(p);
            }
        }

        return new FlexiblePersonRefList { Items = list.ToArray() };
    }
}
