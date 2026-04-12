using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes <see cref="FlexibleHandleList"/> from a JSON string, array, or null.
/// </summary>
public sealed class FlexibleHandleListJsonConverter : JsonConverter<FlexibleHandleList?>
{
    private static readonly char[] Delimiters = [',', ';', '|', '\n', '\r', '\t'];

    public override FlexibleHandleList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                var handles = ParseFromString(s);
                return handles is null ? null : new FlexibleHandleList { Handles = handles };
            }
            case JsonTokenType.StartArray:
                return ReadArray(ref reader);
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} for handle list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleHandleList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var h in value.Handles)
            writer.WriteStringValue(h);
        writer.WriteEndArray();
    }

    private static FlexibleHandleList? ReadArray(ref Utf8JsonReader reader)
    {
        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var h = HandleElementReader.ReadHandleElement(ref reader);
            if (!string.IsNullOrWhiteSpace(h))
                list.Add(h.Trim());
        }

        return new FlexibleHandleList { Handles = list.ToArray() };
    }

    /// <summary>
    /// Whitespace-only → null (omit). Leading <c>[</c> → JSON array. Otherwise delimiter-split (one handle with no delimiters → one element).
    /// </summary>
    internal static string[]? ParseFromString(string? s)
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
                return ParseJsonArray(doc.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var parts = s.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    private static string[] ParseJsonArray(JsonElement array)
    {
        var list = new List<string>();
        foreach (var el in array.EnumerateArray())
        {
            var h = HandleElementReader.ReadHandleFromElement(el);
            if (!string.IsNullOrWhiteSpace(h))
                list.Add(h.Trim());
        }

        return list.ToArray();
    }
}
