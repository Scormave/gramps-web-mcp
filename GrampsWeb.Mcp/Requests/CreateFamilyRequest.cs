using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new family.
/// </summary>
public class CreateFamilyRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Family";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("father_handle")]
    public string? FatherHandle { get; set; }

    [JsonPropertyName("mother_handle")]
    public string? MotherHandle { get; set; }

    [JsonPropertyName("child_ref_list")]
    public GrampsChildRef[]? ChildRefList { get; set; }

    [JsonPropertyName("event_ref_list")]
    public EventRefRequest[]? EventRefList { get; set; }

    [JsonPropertyName("media_list")]
    public MediaRefRequest[]? MediaList { get; set; }

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

    [JsonPropertyName("type")]
    public string? Relationship { get; set; }
}
