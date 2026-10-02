using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Place payload on timeline entries (<c>GET .../timeline</c>); Gramps returns a PlaceProfile object, not a plain string.
/// </summary>
public class GrampsTimelinePlaceProfile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

/// <summary>
/// Person the timeline event belongs to (OpenAPI <c>TimelinePersonProfile</c>).
/// For the anchor person's own events the API sends only <c>relationship: "self"</c>.
/// </summary>
public class GrampsTimelinePersonProfile
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    private string? _nameDisplay;

    /// <summary>Name in the tree's display format, cleaned by <see cref="GrampsNameDisplay.Clean"/>.</summary>
    [JsonPropertyName("name_display")]
    public string? NameDisplay { get => _nameDisplay; set => _nameDisplay = GrampsNameDisplay.Clean(value); }

    /// <summary>Relationship to the anchor person, e.g. "father"; "self" for the anchor or on family timelines.</summary>
    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }

    /// <summary>This person's age at the event.</summary>
    [JsonPropertyName("age")]
    public string? Age { get; set; }
}

/// <summary>
/// Represents a single entry in a Gramps timeline response.
/// Returned by /api/people/{handle}/timeline and /api/families/{handle}/timeline.
/// For places, MCP may synthesize rows from events (backlinks); the bundled API spec does not define <c>/api/places/{handle}/timeline</c>.
/// </summary>
/// <remarks>
/// The timeline API returns <c>date</c> as a <b>formatted display string</b> (see OpenAPI <c>TimelineEventProfile.date</c>),
/// not a structured <see cref="GrampsDate"/> object.
/// </remarks>
public class GrampsTimelineEntry
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    /// <summary>Event label; for relatives' events it names the relationship, e.g. "Death (Father)".</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Event type; may be a string or a typed object in some API payloads.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsWireTypeStringConverter))]
    public string? Type { get; set; }

    /// <summary>Event date as returned by the API (localized display string).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("place")]
    public GrampsTimelinePlaceProfile? Place { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Role of <see cref="Person"/> in the event, e.g. "Primary".</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("person")]
    public GrampsTimelinePersonProfile? Person { get; set; }

    /// <summary>Age of the anchor person at the event; on family timelines, the age of <see cref="Person"/>.</summary>
    [JsonPropertyName("age")]
    public string? Age { get; set; }

    /// <summary>Who took part, for rows MCP builds itself (place timelines); not sent by the API.</summary>
    [JsonIgnore]
    public string? Participants { get; set; }
}
