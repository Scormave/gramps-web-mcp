using System.Text.Json.Serialization;

namespace GrampsWeb.Mcp.Models;

// ──────────────────────────────────────────────────────────────────────────────
// Extended Person  (?extend=all on GET /api/people/{handle})
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Person response enriched with resolved sub-objects when using ?extend=all.
/// All base GrampsPerson fields are inherited; the <see cref="Extended"/> property
/// carries the resolved data.
/// </summary>
public class GrampsPersonExtended : GrampsPerson
{
    [JsonPropertyName("extended")]
    public GrampsPersonExtendedData? Extended { get; set; }
}

/// <summary>
/// Resolved objects returned alongside a person when using ?extend=all.
/// </summary>
public class GrampsPersonExtendedData
{
    [JsonPropertyName("events")]
    public GrampsEvent[]? Events { get; set; }

    /// <summary>Families where this person is a parent/spouse.</summary>
    [JsonPropertyName("families")]
    public GrampsFamily[]? Families { get; set; }

    /// <summary>Families where this person appears as a child.</summary>
    [JsonPropertyName("parent_families")]
    public GrampsFamily[]? ParentFamilies { get; set; }

    [JsonPropertyName("notes")]
    public GrampsNote[]? Notes { get; set; }

    [JsonPropertyName("tags")]
    public GrampsTag[]? Tags { get; set; }

    [JsonPropertyName("citations")]
    public GrampsCitation[]? Citations { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
// Extended Family  (?extend=… on GET /api/families/{handle}, e.g. ?extend=all or ?extend=father_handle,mother_handle)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Family response enriched with resolved sub-objects when using <c>extend</c> query parameters
/// (see apispec: comma-separated keys such as <c>father_handle</c>, <c>mother_handle</c>, or <c>all</c>).
/// </summary>
public class GrampsFamilyExtended : GrampsFamily
{
    [JsonPropertyName("extended")]
    public GrampsFamilyExtendedData? Extended { get; set; }
}

/// <summary>
/// Resolved objects returned alongside a family when using ?extend=all.
/// </summary>
public class GrampsFamilyExtendedData
{
    [JsonPropertyName("events")]
    public GrampsEvent[]? Events { get; set; }

    /// <summary>Fully resolved father object.</summary>
    [JsonPropertyName("father")]
    public GrampsPerson? Father { get; set; }

    /// <summary>Fully resolved mother object.</summary>
    [JsonPropertyName("mother")]
    public GrampsPerson? Mother { get; set; }

    /// <summary>Fully resolved child objects.</summary>
    [JsonPropertyName("children")]
    public GrampsPerson[]? Children { get; set; }

    [JsonPropertyName("notes")]
    public GrampsNote[]? Notes { get; set; }

    [JsonPropertyName("tags")]
    public GrampsTag[]? Tags { get; set; }

    [JsonPropertyName("citations")]
    public GrampsCitation[]? Citations { get; set; }
}
