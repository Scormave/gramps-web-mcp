using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a citation entity from the Gramps genealogy database.
/// </summary>
public class GrampsCitation
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("source_handle")]
    public string? Source { get; set; }

    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public GrampsAttribute[]? AttributeList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
