using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new source.
/// </summary>
public class CreateSourceRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Source";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("pubinfo")]
    public string? PubInfo { get; set; }

    [JsonPropertyName("abbrev")]
    public string? Abbrev { get; set; }

    [JsonPropertyName("media_list")]
    public MediaRefRequest[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("reporef_list")]
    public object[]? RepositoryRefList { get; set; }

    [JsonPropertyName("attribute_list")]
    public AttributeRequest[]? AttributeList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
