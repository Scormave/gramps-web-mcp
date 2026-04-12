using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for POST/PUT person; aligns with Gramps Web API Person JSON where applicable.
/// </summary>
public class CreatePersonRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Person";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("gender")]
    public int Gender { get; set; } = 2;

    [JsonPropertyName("primary_name")]
    public GrampsNameRequest? PrimaryName { get; set; }

    [JsonPropertyName("alternate_names")]
    public GrampsNameRequest[]? AlternateNames { get; set; }

    [JsonPropertyName("event_ref_list")]
    public EventRefRequest[]? EventRefList { get; set; }

    [JsonPropertyName("family_list")]
    public string[]? FamilyList { get; set; }

    [JsonPropertyName("parent_family_list")]
    public FamilyRefRequest[]? ParentFamilyList { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("address_list")]
    public GrampsAddress[]? AddressList { get; set; }

    [JsonPropertyName("attribute_list")]
    public AttributeRequest[]? AttributeList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("urls")]
    public GrampsUrl[]? UrlList { get; set; }

    [JsonPropertyName("person_ref_list")]
    public GrampsPersonRef[]? PersonRefList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}

/// <summary>Gramps Web API Name object shape for request bodies.</summary>
public class GrampsNameRequest
{
    [JsonPropertyName("call")]
    public string? Call { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("date")]
    public DateRequest? Date { get; set; }

    [JsonPropertyName("display_as")]
    public int DisplayAs { get; set; }

    [JsonPropertyName("famnick")]
    public string? FamNick { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("group_as")]
    public string? GroupAs { get; set; }

    [JsonPropertyName("nick")]
    public string? Nick { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("sort_as")]
    public int SortAs { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("surname_list")]
    public SurnameRequest[]? SurnameList { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public class SurnameRequest
{
    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("connector")]
    public string? Connector { get; set; }

    [JsonPropertyName("origintype")]
    public string? OriginType { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; } = true;
}

[JsonConverter(typeof(DateRequestJsonConverter))]
public class DateRequest
{
    public int Calendar { get; set; }

    public int Modifier { get; set; }

    public int Quality { get; set; }

    public string? Text { get; set; }

    public int NewYear { get; set; }

    public int Day { get; set; }

    public int Month { get; set; }

    public int Year { get; set; }

    public bool Slash { get; set; }

    public int EndDay { get; set; }

    public int EndMonth { get; set; }

    public int EndYear { get; set; }

    public bool EndSlash { get; set; }
}

public class EventRefRequest
{
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("attribute_list")]
    public object[]? AttributeList { get; set; }
}

public class FamilyRefRequest
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

public class AttributeRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
