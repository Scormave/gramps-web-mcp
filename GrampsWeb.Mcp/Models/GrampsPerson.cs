using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents an event reference with role and related information.
/// </summary>
public class GrampsEventRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Event role (JSON key <c>role</c>). See <see cref="GrampsWireTypeObject"/>.</summary>
    [JsonPropertyName("role")]
    [JsonConverter(typeof(GrampsWireTypeStringConverter))]
    public string? Role { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public object[]? AttributeList { get; set; }
}

/// <summary>
/// Represents an attribute with type and value.
/// </summary>
public class GrampsAttribute
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Represents a family link reference.
/// </summary>
[JsonConverter(typeof(GrampsFamilyRefJsonConverter))]
public class GrampsFamilyRef
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }

    [JsonPropertyName("frel")]
    public string? FatherRelationship { get; set; }

    [JsonPropertyName("mrel")]
    public string? MotherRelationship { get; set; }
}

/// <summary>
/// Represents a person entity from the Gramps genealogy database.
/// </summary>
public class GrampsPerson
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    /// <summary>Gramps API: 0=Female, 1=Male, 2=Unknown. MCP create/update tools accept names (Female/Male/Unknown).</summary>
    [JsonPropertyName("gender")]
    public int Gender { get; set; } = 2;

    [JsonPropertyName("primary_name")]
    public GrampsName? PrimaryName { get; set; }

    [JsonPropertyName("alternate_names")]
    public GrampsName[]? AlternateNames { get; set; }

    [JsonPropertyName("event_ref_list")]
    public GrampsEventRef[]? EventRefList { get; set; }

    [JsonPropertyName("family_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? FamilyList { get; set; }

    [JsonPropertyName("parent_family_list")]
    public GrampsFamilyRef[]? ParentFamilyList { get; set; }

    [JsonPropertyName("media_list")]
    [JsonConverter(typeof(GrampsHandleStringArrayConverter))]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("address_list")]
    public object[]? AddressList { get; set; }

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
}
