using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Gramps Web API JSON shape for <c>media_list</c> items (see <c>gramps.gen.lib.MediaRef.get_schema</c>).
/// Mutations must send objects, not bare media handles as strings — otherwise jsonschema validation fails.
/// </summary>
public sealed class MediaRefRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "MediaRef";

    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public object[]? AttributeList { get; set; }

    /// <summary>Image region [x,y,w,h]; omit when not cropping.</summary>
    [JsonPropertyName("rect")]
    public int[]? Rect { get; set; }
}
