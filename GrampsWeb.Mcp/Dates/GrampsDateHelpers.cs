using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Dates;

/// <summary>Shared helpers for Gramps date emptiness checks used by mapping and formatters.</summary>
public static class GrampsDateHelpers
{
    /// <summary>
    /// True when <paramref name="date"/> is null or carries no meaningful calendar/text content
    /// (e.g. empty <c>{}</c> objects from the API).
    /// </summary>
    public static bool IsEmpty(GrampsDate? date)
    {
        if (date is null)
            return true;

        if (date.Modifier == 6)
            return string.IsNullOrWhiteSpace(date.Text);

        if (!string.IsNullOrWhiteSpace(date.Text))
            return false;

        if (date.Calendar != 0 || date.Quality != 0 || date.NewYear != 0)
            return false;

        if (date.Modifier != 0)
            return false;

        if (date.Day != 0 || date.Month != 0 || date.Year != 0 || date.Slash)
            return false;

        return date.EndDay == 0 && date.EndMonth == 0 && date.EndYear == 0 && !date.EndSlash;
    }
}
