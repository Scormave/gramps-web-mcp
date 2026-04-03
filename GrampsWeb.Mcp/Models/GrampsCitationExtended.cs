using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

// ──────────────────────────────────────────────────────────────────────────────
// Extended Citation (?extend=source_handle on GET /api/citations/…)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Citation response with resolved sub-objects when using <c>extend</c> (see apispec: <c>source_handle</c> for full source record).
/// </summary>
public class GrampsCitationExtended : GrampsCitation
{
    [JsonPropertyName("extended")]
    public GrampsCitationExtendedData? Extended { get; set; }
}

/// <summary>
/// Resolved objects returned alongside a citation when using <c>extend=source_handle</c> (or <c>all</c>).
/// </summary>
public class GrampsCitationExtendedData
{
    [JsonPropertyName("source")]
    public GrampsSource? Source { get; set; }

    [JsonPropertyName("media")]
    public GrampsMedia[]? Media { get; set; }

    [JsonPropertyName("notes")]
    public GrampsNote[]? Notes { get; set; }

    [JsonPropertyName("tags")]
    public GrampsTag[]? Tags { get; set; }
}
