using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Serialization;

public sealed class DateRequestJsonConverter : JsonConverter<DateRequest>
{
    public override DateRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var d = new DateRequest();
        GrampsDateWireCodec.ReadIntoDateRequest(ref reader, d);
        return d;
    }

    public override void Write(Utf8JsonWriter writer, DateRequest? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        GrampsDateWireCodec.WriteDateRequest(writer, value, options);
    }
}
