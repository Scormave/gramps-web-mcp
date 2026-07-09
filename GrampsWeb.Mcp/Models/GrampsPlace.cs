using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a geographic place in the Gramps genealogy database.
/// </summary>
public class GrampsPlace
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("name")]
    public GrampsPlaceName? PrimaryName { get; set; }

    /// <summary>Display name (<see cref="GrampsPlaceName.Value"/>).</summary>
    [JsonIgnore]
    public string? Name
    {
        get => PrimaryName?.Value;
        set => PrimaryName = value == null ? null : new GrampsPlaceName { Value = value };
    }

    [JsonPropertyName("place_type")]
    [JsonConverter(typeof(GrampsWireTypeStringConverter))]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("long")]
    public string? Longitude { get; set; }

    [JsonPropertyName("lat")]
    public string? Latitude { get; set; }

    [JsonPropertyName("media_list")]
    [JsonConverter(typeof(GrampsMediaRefArrayConverter))]
    public GrampsMediaRef[]? MediaList { get; set; }

    [JsonPropertyName("note_list")]
    public string[]? NoteList { get; set; }

    [JsonPropertyName("citation_list")]
    public string[]? CitationList { get; set; }

    [JsonPropertyName("change")]
    public long? Change { get; set; }

    [JsonPropertyName("private")]
    public bool Private { get; set; }

    [JsonPropertyName("tag_list")]
    public string[]? TagList { get; set; }

    [JsonPropertyName("placeref_list")]
    [JsonConverter(typeof(GrampsPlaceRefArrayConverter))]
    public GrampsPlaceRef[]? PlaceRefList { get; set; }

    [JsonPropertyName("alt_names")]
    public GrampsPlaceName[]? AlternateNames { get; set; }

    [JsonPropertyName("alt_loc")]
    public object[]? AlternateLocations { get; set; }
}
