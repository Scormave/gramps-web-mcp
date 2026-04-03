using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new repository.
/// </summary>
public class CreateRepositoryRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Repository";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("email_list")]
    public string[]? EmailList { get; set; }

    [JsonPropertyName("address_list")]
    public object[]? AddressList { get; set; }

    [JsonPropertyName("urls")]
    public object[]? UrlList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
