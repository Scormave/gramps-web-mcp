using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for PUT media metadata (path/mime preserved from GET; binary upload not via MCP).
/// </summary>
public class CreateMediaRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Media";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("mime")]
    public string? Mime { get; set; }

    [JsonPropertyName("desc")]
    public string? Description { get; set; }

    [JsonPropertyName("date")]
    public DateRequest? Date { get; set; }

    [JsonPropertyName("attribute_list")]
    public AttributeRequest[]? AttributeList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
