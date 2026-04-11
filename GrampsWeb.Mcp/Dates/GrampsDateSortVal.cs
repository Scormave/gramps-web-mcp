using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// Computes Gramps <c>Date.sortval</c> (serial day number) for API mutation bodies.
/// Gramps normally recalculates this in Python; Gramps Web may persist JSON without recalc, leaving <c>sortval</c> at 0.
/// </summary>
/// <remarks>
/// Matches <c>gramps.gen.lib.gcalendar.gregorian_sdn</c> for Gregorian calendar. Other calendars are not supported here.
/// </remarks>
internal static class GrampsDateSortVal
{
    private const int ModTextOnly = 6;

    private const int GrgSdnOffset = 32045;
    private const int GrgDaysPer5Months = 153;
    private const int GrgDaysPer4Years = 1461;
    private const int GrgDaysPer400Years = 146097;

    /// <summary>
    /// Returns a sort value to send on the wire, or <c>null</c> to omit (let server handle text-only / non-Gregorian).
    /// </summary>
    public static int? TryComputeForDateRequest(DateRequest d)
    {
        if (d.Modifier == ModTextOnly)
            return null;

        if (d.Calendar != 0)
            return null;

        var y = d.Year;
        var m = d.Month;
        var day = d.Day;

        if (y == 0 && m == 0 && day == 0)
            return null;

        // Gramps Date._zero_adjust_ymd
        y = y == 0 ? 1 : y;
        m = m < 1 ? 1 : m;
        day = day < 1 ? 1 : day;

        return GregorianSdn(y, m, day);
    }

    /// <summary>
    /// Sort key for timeline-style filtering: prefers wire <see cref="GrampsDate.SortVal"/>, else Gregorian first segment (calendar 0, not text-only).
    /// Returns <c>null</c> when no comparable key; <c>0</c> means undated in Gramps.
    /// </summary>
    internal static int? TryGetTimelineSortKey(GrampsDate? d)
    {
        if (d == null)
            return null;
        if (d.SortVal.HasValue)
            return d.SortVal.Value;
        if (d.Calendar != 0)
            return null;
        if (d.Modifier == ModTextOnly)
            return null;

        var y = d.Year;
        var m = d.Month;
        var day = d.Day;
        if (y == 0 && m == 0 && day == 0)
            return null;

        y = y == 0 ? 1 : y;
        m = m < 1 ? 1 : m;
        day = day < 1 ? 1 : day;

        return GregorianSdn(y, m, day);
    }

    /// <summary>Gregorian serial day for a calendar date (after Gramps zero-adjust).</summary>
    internal static int? TryGregorianSdnYmd(int year, int month, int day)
    {
        if (year == 0 && month == 0 && day == 0)
            return null;
        var y = year == 0 ? 1 : year;
        var m = month < 1 ? 1 : month;
        var d = day < 1 ? 1 : day;
        return GregorianSdn(y, m, d);
    }

    /// <summary>Port of <c>gregorian_sdn</c> from Gramps <c>gcalendar.py</c>.</summary>
    private static int GregorianSdn(int year, int month, int day)
    {
        if (year < 0)
            year += 4801;
        else
            year += 4800;

        if (month > 2)
            month -= 3;
        else
        {
            month += 9;
            year -= 1;
        }

        return ((year / 100) * GrgDaysPer400Years) / 4
            + ((year % 100) * GrgDaysPer4Years) / 4
            + (month * GrgDaysPer5Months + 2) / 5
            + day
            - GrgSdnOffset;
    }
}
