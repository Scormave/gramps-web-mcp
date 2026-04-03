using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new citation.
/// </summary>
public class CreateCitationRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Citation";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("source_handle")]
    public string? Source { get; set; }

    [JsonPropertyName("page")]
    public string? Page { get; set; }

    [JsonPropertyName("confidence")]
    public int Confidence { get; set; }

    [JsonPropertyName("date")]
    public DateRequest? Date { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public AttributeRequest[]? AttributeList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
