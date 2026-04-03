using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

// ──────────────────────────────────────────────────────────────────────────────
// Extended Event (?extend=place, ?extend=all, … on GET /api/events/…)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Event response with resolved sub-objects when using <c>extend</c> query parameters
/// (see apispec: e.g. <c>place</c> for the full place record, or <c>all</c>).
/// </summary>
public class GrampsEventExtended : GrampsEvent
{
    [JsonPropertyName("extended")]
    public GrampsEventExtendedData? Extended { get; set; }
}

/// <summary>
/// Resolved objects returned alongside an event when using <c>extend</c>.
/// Only <see cref="Place"/> is required for <c>extend=place</c>; other properties appear with broader <c>extend</c> values.
/// </summary>
public class GrampsEventExtendedData
{
    [JsonPropertyName("place")]
    public GrampsPlace? Place { get; set; }

    [JsonPropertyName("citations")]
    public GrampsCitation[]? Citations { get; set; }

    [JsonPropertyName("media")]
    public GrampsMedia[]? Media { get; set; }

    [JsonPropertyName("notes")]
    public GrampsNote[]? Notes { get; set; }

    [JsonPropertyName("tags")]
    public GrampsTag[]? Tags { get; set; }
}
