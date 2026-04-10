using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes Gramps Web <c>Name.type</c> (Birth Name, Married Name, etc.).
/// </summary>
/// <remarks>
/// <para><b>GET</b> responses often use a plain string.</para>
/// <para><b>POST/PUT mutation</b> responses return a structured object, e.g.:</para>
/// <code>
/// "type": {
///   "_class": "NameType",
///   "string": "",
///   "value": 2
/// }
/// </code>
/// <para>When <c>string</c> is empty, <c>value</c> is the Gramps name-type enum; we surface it as a string for <see cref="Models.GrampsName.Type"/>.</para>
/// </remarks>
public sealed class GrampsNameTypeObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromNameTypeObject(ref reader),
            _ => throw new JsonException(
                $"Unexpected JSON token for Name.type: {reader.TokenType}.")
        };
    }

    private static string? ReadFromNameTypeObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (root.TryGetProperty("string", out var str) && str.ValueKind == JsonValueKind.String)
        {
            var s = str.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        if (root.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetRawText();

        return null;
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
