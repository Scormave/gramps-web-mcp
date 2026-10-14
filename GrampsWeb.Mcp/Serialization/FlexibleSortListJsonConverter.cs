using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes <see cref="FlexibleSortList"/> from a JSON string, array, or null. Other values and array items that
/// are not strings are kept as their JSON text, so the tool can name them in its error.
/// </summary>
public sealed class FlexibleSortListJsonConverter : JsonConverter<FlexibleSortList?>
{
    public override FlexibleSortList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return FlexibleHandleListJsonConverter.ParseFromString(reader.GetString()) is { } names
                    ? new FlexibleSortList { Names = names }
                    : null;
            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var name = NameOf(JsonElement.ParseValue(ref reader));
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(name.Trim());
                }

                return new FlexibleSortList { Names = list.ToArray() };
            }
            default:
                return new FlexibleSortList { Names = [NameOf(JsonElement.ParseValue(ref reader))] };
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleSortList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var name in value.Names)
            writer.WriteStringValue(name);
        writer.WriteEndArray();
    }

    private static string NameOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();
}
