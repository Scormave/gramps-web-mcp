using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads <c>placeref_list</c> items as plain handle strings or <c>{ref, date}</c> objects.
/// </summary>
public sealed class GrampsPlaceRefJsonConverter : JsonConverter<GrampsPlaceRef?>
{
    public override GrampsPlaceRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsPlaceRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ReadFromElement(doc.RootElement, options);
        }

        throw new JsonException($"Unexpected JSON token for {nameof(GrampsPlaceRef)}: {reader.TokenType}.");
    }

    internal static GrampsPlaceRef? ReadFromElement(JsonElement root, JsonSerializerOptions options)
    {
        if (root.ValueKind == JsonValueKind.String)
            return new GrampsPlaceRef { Ref = root.GetString() };

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        GrampsDate? date = null;
        if (root.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.Object)
            date = dateEl.Deserialize<GrampsDate>(options);

        return new GrampsPlaceRef
        {
            Ref = HandleElementReader.ReadHandleFromElement(root),
            Date = date
        };
    }

    public override void Write(Utf8JsonWriter writer, GrampsPlaceRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (value.Date != null)
        {
            writer.WritePropertyName("date");
            JsonSerializer.Serialize(writer, value.Date, options);
        }

        writer.WriteEndObject();
    }
}
