using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Gramps may serialize family refs as either an object ({ref, frel, mrel...}) or a plain string handle.
/// </summary>
public sealed class GrampsFamilyRefJsonConverter : JsonConverter<GrampsFamilyRef?>
{
    public override GrampsFamilyRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
            return new GrampsFamilyRef { Ref = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            return new GrampsFamilyRef
            {
                Ref = GetString(root, "ref"),
                Relationship = GetString(root, "relationship"),
                FatherRelationship = GetString(root, "frel"),
                MotherRelationship = GetString(root, "mrel")
            };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(GrampsFamilyRef)}");
    }

    public override void Write(Utf8JsonWriter writer, GrampsFamilyRef? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (!string.IsNullOrEmpty(value.Ref))
            writer.WriteString("ref", value.Ref);
        if (!string.IsNullOrEmpty(value.Relationship))
            writer.WriteString("relationship", value.Relationship);
        if (!string.IsNullOrEmpty(value.FatherRelationship))
            writer.WriteString("frel", value.FatherRelationship);
        if (!string.IsNullOrEmpty(value.MotherRelationship))
            writer.WriteString("mrel", value.MotherRelationship);
        writer.WriteEndObject();
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }
}
