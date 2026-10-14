using System.Text.Json;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Deserializes <see cref="FlexibleMediaLinkList"/> from a JSON string, array, or null. Strings are read as
/// handle lists (<see cref="FlexibleHandleListJsonConverter"/>); array entries may be handles or objects with
/// <c>ref</c> / <c>handle</c> / <c>id</c> and <c>region</c> / <c>rect</c>.
/// </summary>
public sealed class FlexibleMediaLinkListJsonConverter : JsonConverter<FlexibleMediaLinkList?>
{
    public override FlexibleMediaLinkList? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return ParseFromString(reader.GetString());
            case JsonTokenType.StartArray:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return FromArray(doc.RootElement);
            }
            default:
                throw new JsonException(
                    $"Unexpected token {reader.TokenType} for media link list; use JSON array, string, or null.");
        }
    }

    public override void Write(Utf8JsonWriter writer, FlexibleMediaLinkList? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var link in value.Items)
        {
            if (link.Region is null)
            {
                writer.WriteStringValue(link.Ref);
                continue;
            }

            writer.WriteStartObject();
            writer.WriteString("ref", link.Ref);
            writer.WritePropertyName("region");
            writer.WriteStartArray();
            foreach (var edge in link.Region.ToArray())
                writer.WriteNumberValue(edge);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>Whitespace-only → null (omit). Leading <c>[</c> → JSON array. Otherwise a delimiter-separated handle list.</summary>
    private static FlexibleMediaLinkList? ParseFromString(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();
        if (s[0] == '[')
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.ValueKind == JsonValueKind.Array ? FromArray(doc.RootElement) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var handles = FlexibleHandleListJsonConverter.ParseFromString(s);
        return handles is null ? null : new FlexibleMediaLinkList { Items = handles.Select(h => new MediaLink(h)).ToArray() };
    }

    private static FlexibleMediaLinkList FromArray(JsonElement array)
    {
        var list = new List<MediaLink>();
        foreach (var element in array.EnumerateArray())
        {
            if (ReadLink(element) is { } link)
                list.Add(link);
        }

        return new FlexibleMediaLinkList { Items = list.ToArray() };
    }

    private static MediaLink? ReadLink(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(element.GetString()) ? null : new MediaLink(element.GetString()!.Trim());
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var handle = JsonElementPropertyReader.GetString(element, "ref", "handle", "id");
        if (string.IsNullOrWhiteSpace(handle))
            return null;

        var regionElement = FindProperty(element, "region", "rect");
        if (regionElement is null)
            return new MediaLink(handle.Trim());
        return MediaRegion.TryRead(regionElement.Value, out var region, out var error)
            ? new MediaLink(handle.Trim(), region)
            : new MediaLink(handle.Trim(), RegionError: error);
    }

    private static JsonElement? FindProperty(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return property.Value;
        }

        return null;
    }
}
