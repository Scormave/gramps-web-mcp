using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a repository entity from the Gramps genealogy database.
/// </summary>
public class GrampsRepository
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsWireTypeStringConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("email_list")]
    public string[]? EmailList { get; set; }

    [JsonPropertyName("address_list")]
    public object[]? AddressList { get; set; }

    [JsonPropertyName("urls")]
    public object[]? UrlList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
