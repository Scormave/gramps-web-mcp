using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>Deserializes <see cref="FlexibleFilterRules"/> from any JSON value; null stays null.</summary>
public sealed class FlexibleFilterRulesJsonConverter : JsonConverter<FlexibleFilterRules?>
{
    public override FlexibleFilterRules? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? null
            : new FlexibleFilterRules { Value = JsonElement.ParseValue(ref reader) };

    public override void Write(Utf8JsonWriter writer, FlexibleFilterRules? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            value.Value.WriteTo(writer);
    }
}
