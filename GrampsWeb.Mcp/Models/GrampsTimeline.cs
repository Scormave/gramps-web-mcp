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

    /// <summary>Human-oriented event label (e.g. includes relationship); preferred for display when set.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Event type; may be a string or a typed object in some API payloads.</summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(GrampsEventTypeObjectConverter))]
    public string? Type { get; set; }

    /// <summary>Event date as returned by the API (localized display string).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("place")]
    public GrampsTimelinePlaceProfile? Place { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Display name of the person the event belongs to (for relative events).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Event category, e.g. "vital", "family", "vocational".</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Average citation confidence (0=very low … 4=very high). Populated when the timeline API returns it.</summary>
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
}
