using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a child reference within a family (child_ref_list item).
/// frel = father's relationship type to this child; mrel = mother's.
/// Values come from child_ref_types: Birth, Adopted, Stepchild, Sponsored, Foster, Unknown.
/// </summary>
[JsonConverter(typeof(GrampsChildRefJsonConverter))]
public class GrampsChildRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("frel")]
    public string? FatherRelType { get; set; }

    [JsonPropertyName("mrel")]
    public string? MotherRelType { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }
}

/// <summary>
/// Represents a family entity from the Gramps genealogy database.
/// </summary>
public class GrampsFamily
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("father_handle")]
    public string? FatherHandle { get; set; }

    [JsonPropertyName("mother_handle")]
    public string? MotherHandle { get; set; }

    [JsonPropertyName("child_ref_list")]
    public GrampsChildRef[]? ChildRefList { get; set; }

    [JsonPropertyName("event_ref_list")]
    public GrampsEventRef[]? EventRefList { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("attribute_list")]
    public GrampsAttribute[]? AttributeList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    /// <summary>Relationship between parents (Gramps Web JSON key <c>type</c>). See <see cref="Serialization.GrampsFamilyRelTypeObjectConverter"/>.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsFamilyRelTypeObjectConverter))]
    public string? Relationship { get; set; }
}
