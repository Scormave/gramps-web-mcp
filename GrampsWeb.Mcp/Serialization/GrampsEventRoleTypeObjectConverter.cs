using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes Gramps Web <c>EventRef.role</c> (Primary, Family, etc.).
/// </summary>
/// <remarks>
/// <para>Requests and some GET payloads use a string, e.g. <c>"role": "Primary"</c>.</para>
/// <para><b>Mutation</b> responses may return:</para>
/// <code>
/// "role": {
///   "_class": "EventRoleType",
///   "string": "",
///   "value": 1
/// }
/// </code>
/// <para>When <c>string</c> is empty, <c>value</c> is the Gramps role enum; we surface it as a string for <see cref="Models.GrampsEventRef.Role"/>.</para>
/// </remarks>
public sealed class GrampsEventRoleTypeObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromEventRoleTypeObject(ref reader),
            _ => throw new JsonException(
                $"Unexpected JSON token for EventRef.role: {reader.TokenType}.")
        };
    }

    private static string? ReadFromEventRoleTypeObject(ref Utf8JsonReader reader)
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
