using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes Gramps Web <c>Place.place_type</c>.
/// </summary>
/// <remarks>
/// <para><b>GET</b> responses typically use a plain string (e.g. <c>"City"</c>).</para>
/// <para><b>POST/PUT mutation</b> responses return a structured object, e.g.:</para>
/// <code>
/// "place_type": { "_class": "PlaceType", "string": "", "value": 1 }
/// </code>
/// </remarks>
public sealed class GrampsPlaceTypeObjectConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.StartObject => ReadFromPlaceTypeObject(ref reader),
            _ => throw new JsonException($"Unexpected JSON token for place_type: {reader.TokenType}.")
        };
    }

    private static string? ReadFromPlaceTypeObject(ref Utf8JsonReader reader)
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
