using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

public sealed class GrampsDateJsonConverter : JsonConverter<GrampsDate>
{
    public override GrampsDate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var d = new GrampsDate();
        GrampsDateWireCodec.ReadIntoGrampsDate(ref reader, d);
        return d;
    }

    public override void Write(Utf8JsonWriter writer, GrampsDate? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        GrampsDateWireCodec.WriteGrampsDate(writer, value, options);
    }
}
