using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>Postal address on a Person (Gramps Web <c>address_list</c> items).</summary>
public class GrampsAddress
{
    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("locality")]
    public string? Locality { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("county")]
    public string? County { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("postal")]
    public string? Postal { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }

    [JsonPropertyName("citation_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("note_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
