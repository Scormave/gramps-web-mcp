using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a media object from the Gramps genealogy database.
/// </summary>
public class GrampsMedia
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }

    [JsonPropertyName("attribute_list")]
    public GrampsAttribute[]? AttributeList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
