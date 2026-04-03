using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

/// <summary>
/// Represents a single entry in a Gramps timeline response.
/// Returned by /api/people/{handle}/timeline, /api/families/{handle}/timeline,
/// and /api/places/{handle}/timeline.
/// </summary>
public class GrampsTimelineEntry
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("gramps_id")]
    public string? GrampsId { get; set; }

    /// <summary>Event type string, e.g. "Birth", "Marriage", "Death".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("date")]
    public GrampsDate? Date { get; set; }

    /// <summary>Place handle or resolved place name, depending on API version.</summary>
    [JsonPropertyName("place")]
    public string? Place { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Event role, e.g. "Primary", "Witness", "Clergy".</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>Display name of the person the event belongs to (for relative events).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Event category, e.g. "vital", "family", "vocational".</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Average citation confidence (0=very low … 4=very high). Present when ratings=true.</summary>
    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
}
