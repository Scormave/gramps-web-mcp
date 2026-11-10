using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Serialization;

/// <summary>
/// Reads and writes the <c>attribute_list</c> of an event or media link. The list accepts the forms of
/// <see cref="Input.FlexibleAttributeList"/>, under <c>attributes</c>, <c>attribute_list</c> or <c>attributeList</c>,
/// and is written as Gramps <c>Attribute</c> objects that leave out citations, notes and privacy when unset, so a
/// stored attribute matched by type and value keeps its own (see <see cref="Tools.GrampsObjectPatch"/>).
/// </summary>
internal static class GrampsAttributeJson
{
    private static readonly string[] PropertyNames = ["attributes", "attribute_list", "attributeList"];

    /// <summary>The attributes of a link object; null when it has none of the attribute keys or an empty string.</summary>
    public static GrampsAttribute[]? ReadProperty(JsonElement link, JsonSerializerOptions options)
    {
        foreach (var property in link.EnumerateObject())
        {
            if (PropertyNames.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return FlexibleAttributeListJsonConverter.ReadElement(property.Value, options);
        }

        return null;
    }

    public static JsonObject ToNode(GrampsAttribute attribute)
    {
        var node = new JsonObject();
        if (attribute.Type is not null)
            node["type"] = attribute.Type;
        if (attribute.Value is not null)
            node["value"] = attribute.Value;
        if (attribute.CitationList is { Length: > 0 } citations)
            node["citation_list"] = new JsonArray(citations.Select(h => (JsonNode?)h).ToArray());
        if (attribute.NoteList is { Length: > 0 } notes)
            node["note_list"] = new JsonArray(notes.Select(h => (JsonNode?)h).ToArray());
        if (attribute.Private)
            node["private"] = true;
        return node;
    }

    public static JsonArray ToNode(IEnumerable<GrampsAttribute> attributes) =>
        new(attributes.Select(a => (JsonNode?)ToNode(a)).ToArray());

    /// <summary>Writes <c>attribute_list</c> when <paramref name="attributes"/> is not null, an empty list included.</summary>
    public static void WriteProperty(Utf8JsonWriter writer, GrampsAttribute[]? attributes)
    {
        if (attributes is null)
            return;
        writer.WritePropertyName("attribute_list");
        ToNode(attributes).WriteTo(writer);
    }
}
