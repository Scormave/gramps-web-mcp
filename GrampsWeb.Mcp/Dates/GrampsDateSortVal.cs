using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// Computes Gramps <c>Date.sortval</c> (serial day number) for API mutation bodies.
/// Gramps normally recalculates this in Python; Gramps Web may persist JSON without recalc, leaving <c>sortval</c> at 0.
/// </summary>
/// <remarks>
/// Matches Gramps <c>Date.set</c> for every Gramps calendar (<see cref="GrampsCalendars"/>): the date is converted from
/// its calendar, a dual-dated year counts as Julian, and a date on or after the new year day of a year that starts
/// in March or September sorts with the previous year.
/// </remarks>
internal static class GrampsDateSortVal
{
    private const int ModRange = 4;
    private const int ModSpan = 5;
    private const int ModTextOnly = 6;

    /// <summary>
    /// Returns a sort value to send on the wire, or <c>null</c> to omit (let server handle text-only / unknown calendars).
    /// </summary>
    public static int? TryComputeForDateRequest(DateRequest d)
    {
        if (d.Modifier == ModTextOnly)
            return null;

        return TryCompute(d.Calendar, d.NewYear, d.Slash, d.Year, d.Month, d.Day, out _);
    }

    /// <summary>
    /// Sort key for timeline-style filtering: prefers wire <see cref="GrampsDate.SortVal"/>, else computed from the first segment (not text-only).
    /// Returns <c>null</c> when no comparable key; <c>0</c> means undated in Gramps.
    /// </summary>
    internal static int? TryGetTimelineSortKey(GrampsDate? d)
    {
        if (d == null)
            return null;
        if (d.SortVal.HasValue)
            return d.SortVal.Value;
        if (d.Modifier == ModTextOnly)
            return null;

        return TryCompute(d.Calendar, d.NewYear, d.Slash, d.Year, d.Month, d.Day, out _);
    }

    /// <summary>The sort value of a full date in a known calendar, moved into the previous year as its new year says.</summary>
    internal static int SortValueOf(int calendar, int newYear, int year, int month, int day) =>
        TryCompute(calendar, newYear, slash: false, year, month, day, out _)
        ?? throw new ArgumentOutOfRangeException(nameof(calendar), calendar, "Unknown Gramps calendar.");

    /// <summary>
    /// Port of the round trip at the end of Gramps <c>Date.set</c>, which rejects dates that don't exist in their
    /// calendar: the start date is converted back from its sort value, and the end of a range or span from its own
    /// serial day. A month or day of 0 may come back as 1.
    /// </summary>
    /// <remarks>
    /// Gramps adds a year to the end of a range or span when the start falls on or after the new year day, so it
    /// rejects such dates with a March or September new year even when both dates exist.
    /// </remarks>
    internal static bool PassesGrampsDateCheck(DateRequest d)
    {
        if (d.Modifier == ModTextOnly)
            return true;

        var calendar = d.Slash ? GrampsCalendars.Julian : d.Calendar;
        if (!GrampsCalendars.IsKnown(calendar))
            return false;

        var sortVal = TryCompute(calendar, d.NewYear, slash: false, d.Year, d.Month, d.Day, out var yearDelta) ?? 0;
        var (year, month, day) = GrampsCalendars.FromSdn(calendar, sortVal);
        if (!RoundTripMatches(year, month, day, d.Year, d.Month, d.Day, yearDelta))
            return false;

        if (d.Modifier is not (ModRange or ModSpan))
            return true;

        var endSdn = ZeroAdjustedSdn(calendar, d.EndYear, d.EndMonth, d.EndDay);
        var (endYear, endMonth, endDay) = GrampsCalendars.FromSdn(calendar, endSdn);
        return RoundTripMatches(endYear, endMonth, endDay, d.EndYear, d.EndMonth, d.EndDay, yearDelta);
    }

    /// <summary>
    /// Gramps <c>Date._calc_sort_value</c> and <c>Date._adjust_newyear</c>; <paramref name="yearDelta"/> is -1 when the
    /// new year moved the date into the previous year.
    /// </summary>
    private static int? TryCompute(int calendar, int newYear, bool slash, int year, int month, int day, out int yearDelta)
    {
        yearDelta = 0;
        if (slash)
            calendar = GrampsCalendars.Julian;
        if (!GrampsCalendars.IsKnown(calendar))
            return null;
        if (year == 0 && month == 0 && day == 0)
            return null;

        // Gramps compares the stored month and day, so a month-only date in March isn't moved by a 1 March new year.
        if (NewYearSplit(newYear) is { } split && (month, day).CompareTo(split) >= 0)
        {
            yearDelta = -1;
            year -= 1;
        }

        return ZeroAdjustedSdn(calendar, year, month, day);
    }

    /// <summary>The month and day the year opens on for new year Mar1, Mar25, or Sep1; <c>null</c> for 1 January.</summary>
    internal static (int Month, int Day)? NewYearSplit(int newYear) => newYear switch
    {
        1 => (3, 1),
        2 => (3, 25),
        3 => (9, 1),
        _ => null
    };

    /// <summary>Gramps <c>Date._zero_adjust_ymd</c> followed by the calendar conversion.</summary>
    private static int ZeroAdjustedSdn(int calendar, int year, int month, int day) =>
        GrampsCalendars.ToSdn(calendar, year == 0 ? 1 : year, Math.Max(month, 1), Math.Max(day, 1));

    /// <summary>Gramps <c>Date.__compare</c> for one date of the round trip.</summary>
    private static bool RoundTripMatches(int year, int month, int day, int originalYear, int originalMonth, int originalDay, int yearDelta) =>
        Same(day, originalDay) && Same(month, originalMonth) && Same(year - yearDelta, originalYear);

    private static bool Same(int adjusted, int original) => adjusted == original || (original == 0 && adjusted == 1);
}
