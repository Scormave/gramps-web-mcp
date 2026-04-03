using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a note entity from the Gramps genealogy database.
/// </summary>
public class GrampsNote
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("text")]
    [JsonConverter(typeof(GrampsNoteTextStringConverter))]
    public string? Text { get; set; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsNoteTypeStringConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("format")]
    public int Format { get; set; } // 0=plain text, 1=flowed

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }
}
