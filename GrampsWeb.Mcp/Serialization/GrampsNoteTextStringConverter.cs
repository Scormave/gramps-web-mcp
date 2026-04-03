using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Gramps Web returns <c>Note.text</c> as <c>StyledText</c> (<c>{"string":"...","tags":[]}</c> per apispec) or sometimes a plain string.
/// MCP keeps a single <see cref="Models.GrampsNote.Text"/> string for display and tools.
/// </summary>
public sealed class GrampsNoteTextStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromStyledTextObject(ref reader),
            _ => throw new JsonException($"Unexpected JSON token for note text: {reader.TokenType}.")
        };
    }

    private static string? ReadFromStyledTextObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        // Swagger 2 StyledText uses property name "string" for the body.
        if (doc.RootElement.TryGetProperty("string", out var body))
        {
            return body.ValueKind switch
            {
                JsonValueKind.String => body.GetString(),
                JsonValueKind.Null => null,
                _ => body.ToString()
            };
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
