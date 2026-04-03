using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Reference from a source to a repository (reporef_list item).
/// </summary>
[JsonConverter(typeof(GrampsRepositoryRefJsonConverter))]
public class GrampsRepositoryRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("call_number")]
    public string? CallNumber { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
