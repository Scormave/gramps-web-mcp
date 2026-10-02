using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Person summary from <c>?profile=</c> payloads (OpenAPI <c>PersonProfile</c>).
/// A missing person is sent as an empty object, so every field is optional.
/// </summary>
public class GrampsPersonProfile
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    private string? _nameDisplay;

    /// <summary>Name in the tree's display format, cleaned by <see cref="GrampsNameDisplay.Clean"/>.</summary>
    [JsonPropertyName("name_display")]
    public string? NameDisplay { get => _nameDisplay; set => _nameDisplay = GrampsNameDisplay.Clean(value); }

    [JsonPropertyName("sex")]
    public string? Sex { get; set; }

    /// <summary>Birth, or the best fallback such as baptism.</summary>
    [JsonPropertyName("birth")]
    public GrampsEventProfile? Birth { get; set; }

    /// <summary>Death, or the best fallback such as burial.</summary>
    [JsonPropertyName("death")]
    public GrampsEventProfile? Death { get; set; }
}

/// <summary>Event summary from <c>?profile=</c> payloads (OpenAPI <c>EventProfile</c>).</summary>
public class GrampsEventProfile
{
    /// <summary>Localized event type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Localized display date.</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>Full place title with its hierarchy.</summary>
    [JsonPropertyName("place")]
    public string? Place { get; set; }

    [JsonPropertyName("place_name")]
    public string? PlaceName { get; set; }

    /// <summary>Type and primary participant, e.g. "Birth - Smith, John".</summary>
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Present when the request asked for <c>profile=participants</c>.</summary>
    [JsonPropertyName("participants")]
    public GrampsEventParticipants? Participants { get; set; }
}

/// <summary>People and families that reference an event, with their roles.</summary>
public class GrampsEventParticipants
{
    [JsonPropertyName("people")]
    public GrampsEventPersonParticipant[]? People { get; set; }

    [JsonPropertyName("families")]
    public GrampsEventFamilyParticipant[]? Families { get; set; }
}

public class GrampsEventPersonParticipant
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("person")]
    public GrampsPersonProfile? Person { get; set; }
}

public class GrampsEventFamilyParticipant
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("family")]
    public GrampsFamilyProfile? Family { get; set; }
}

/// <summary>Family summary from <c>?profile=</c> payloads (OpenAPI <c>FamilyProfile</c>).</summary>
public class GrampsFamilyProfile
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("father")]
    public GrampsPersonProfile? Father { get; set; }

    [JsonPropertyName("mother")]
    public GrampsPersonProfile? Mother { get; set; }

    /// <summary>One entry per child_ref_list item, in the same order.</summary>
    [JsonPropertyName("children")]
    public GrampsPersonProfile[]? Children { get; set; }

    [JsonPropertyName("marriage")]
    public GrampsEventProfile? Marriage { get; set; }

    [JsonPropertyName("divorce")]
    public GrampsEventProfile? Divorce { get; set; }

    /// <summary>With <c>profile=events</c>: one entry per event_ref_list item, in the same order.</summary>
    [JsonPropertyName("events")]
    public GrampsEventProfile[]? Events { get; set; }
}

/// <summary>Place summary from <c>?profile=</c> payloads (OpenAPI <c>PlaceProfile</c>).</summary>
public class GrampsPlaceProfile
{
    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Localized place type.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Enclosing places from the nearest upward, following each place's first placeref.</summary>
    [JsonPropertyName("parent_places")]
    public GrampsPlaceProfile[]? ParentPlaces { get; set; }

    /// <summary>One entry per placeref in placeref order; missing parents are skipped.</summary>
    [JsonPropertyName("direct_parent_places")]
    public GrampsDirectParentPlace[]? DirectParentPlaces { get; set; }
}

public class GrampsDirectParentPlace
{
    [JsonPropertyName("place")]
    public GrampsPlaceProfile? Place { get; set; }

    [JsonPropertyName("date_str")]
    public string? DateStr { get; set; }
}

/// <summary>
/// Gramps keeps the separator of an empty name part in <c>name_display</c>, e.g. ", Anna" for a person
/// without a surname in the "Surname, Given" format.
/// </summary>
internal static class GrampsNameDisplay
{
    private static readonly char[] Separators = [' ', ',', '\t', '\r', '\n'];

    public static string? Clean(string? value) => value?.Trim(Separators);
}
