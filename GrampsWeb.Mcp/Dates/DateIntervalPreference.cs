namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// How ambiguous dash date forms map to Gramps interval modifiers.
/// Explicit wording (<c>between</c>, <c>from … to …</c>, <c>from DATE</c>, <c>to DATE</c>, <c>before</c>, <c>after</c>) ignores this.
/// </summary>
public enum DateIntervalPreference
{
    /// <summary>Closed dashes → span (5); open dashes → From (7) / To (8). Default for places.</summary>
    Span = 0,

    /// <summary>Closed dashes → range (4); open dashes → After (2) / Before (1). Used for events, citations, media.</summary>
    Range = 1
}
