using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Requests;

/// <summary>
/// Request body for creating a new place.
/// </summary>
public class CreatePlaceRequest
{
    [JsonPropertyName("_class")]
    public string Class { get; set; } = "Place";

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("name")]
    public PlaceNameRequest? Name { get; set; }

    [JsonPropertyName("place_type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("long")]
    public string? Longitude { get; set; }

    [JsonPropertyName("lat")]
    public string? Latitude { get; set; }

    [JsonPropertyName("media_list")]
    public string[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("placeref_list")]
    public object[]? PlaceRefList { get; set; }

    [JsonPropertyName("alt_loc")]
    public object[]? AlternateLocations { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }
}
