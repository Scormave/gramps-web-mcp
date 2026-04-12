using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>Association to another person (Gramps Web <c>person_ref_list</c> items).</summary>
public class GrampsPersonRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("rel")]
    public string? Relationship { get; set; }

    [JsonPropertyName("citation_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
