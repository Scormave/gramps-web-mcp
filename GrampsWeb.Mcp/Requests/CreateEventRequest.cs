using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new event.
/// </summary>
public class CreateEventRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Event";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("date")]
    public DateRequest? Date { get; set; }

    [JsonPropertyName("place")]
    public string? Place { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("attribute_list")]
    public AttributeRequest[]? AttributeList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
