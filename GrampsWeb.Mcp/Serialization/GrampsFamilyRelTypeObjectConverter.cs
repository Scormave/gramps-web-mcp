using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes Gramps Web <c>Family.type</c> (parent relationship: Married, Unmarried, etc.).
/// </summary>
/// <remarks>
/// <para><b>GET</b> responses often use a plain string, e.g. <c>"type": "Married"</c>.</para>
/// <para><b>POST/PUT mutation</b> responses return a structured object, e.g.:</para>
/// <code>
/// "type": {
///   "_class": "FamilyRelType",
///   "string": "",
///   "value": 0
/// }
/// </code>
/// <para>When <c>string</c> is empty, the numeric <c>value</c> is the Gramps enumeration (0 = Married, etc.); we surface it as a string for <see cref="Models.GrampsFamily.Relationship"/>.</para>
/// </remarks>
public sealed class GrampsFamilyRelTypeObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromFamilyRelTypeObject(ref reader),
            _ => throw new JsonException(
                $"Unexpected JSON token for Family.type (parent relationship): {reader.TokenType}.")
        };
    }

    private static string? ReadFromFamilyRelTypeObject(ref Utf8JsonReader reader)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        // Prefer human-readable label when API sends it (some payloads may populate "string").
        if (root.TryGetProperty("string", out var str) && str.ValueKind == JsonValueKind.String)
        {
            var s = str.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                return s;
        }

        // Mutation responses often have empty "string" and only "value" (enum index).
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
