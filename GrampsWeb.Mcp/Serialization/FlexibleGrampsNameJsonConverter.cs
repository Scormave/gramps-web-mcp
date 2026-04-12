using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class FlexibleGrampsNameJsonConverter : JsonConverter<FlexibleGrampsName?>
{
    public override FlexibleGrampsName? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;
                var name = ParseStringValue(s.Trim());
                return new FlexibleGrampsName { Name = name };
            }
            case JsonTokenType.StartObject:
            {
                var name = JsonSerializer.Deserialize<GrampsName>(ref reader, options)
                    ?? throw new JsonException("Name object deserialized to null.");
                return new FlexibleGrampsName { Name = name };
            }
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} for name; use JSON object or string, or null.");
        }
    }

    /// <summary>First non-empty line when multiple lines are present (e.g. pasted multiline).</summary>
    private static GrampsName ParseStringValue(string s)
    {
        foreach (var raw in s.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length > 0)
                return FlexibleGrampsNameParsing.ParseSimpleLine(raw);
        }

        return FlexibleGrampsNameParsing.ParseSimpleLine(s);
    }

    public override void Write(Utf8JsonWriter writer, FlexibleGrampsName? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value.Name, options);
    }
}
