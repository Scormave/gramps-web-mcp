using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a source entity from the Gramps genealogy database.
/// </summary>
public class GrampsSource
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("pubinfo")]
    public string? PubInfo { get; set; }

    [JsonPropertyName("abbrev")]
    public string? Abbrev { get; set; }

    [JsonPropertyName("media_list")]
    [JsonConverter(typeof(GrampsMediaRefArrayConverter))]
    public GrampsMediaRef[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("reporef_list")]
    public GrampsRepositoryRef[]? RepositoryRefList { get; set; }

    [JsonPropertyName("attribute_list")]
    public GrampsAttribute[]? AttributeList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
