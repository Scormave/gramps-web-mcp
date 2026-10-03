using System.Globalization;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools;

namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// Turns the get_timeline <c>dates</c> filter, written like any other date, into the Gregorian bounds Gramps Web
/// takes (<c>y/m/d-y/m/d</c>, <c>y/m/d-</c>, <c>-y/m/d</c>) and the same bounds as serial days for place timelines.
/// </summary>
/// <remarks>
/// A year or month covers all its days, so <c>1850-1860</c> runs from 1 January 1850 to 31 December 1860. From and
/// to include the date, before and after leave it out. A date in another calendar is converted, so
/// <c>1856-07-20 (Julian)</c> asks Gramps Web for 1856/8/1. The <c>y/m/d</c> form Gramps Web takes still works.
/// </remarks>
internal static class TimelineDateFilter
{
    private const int ModBefore = 1;
    private const int ModAfter = 2;
    private const int ModRange = 4;
    private const int ModSpan = 5;
    private const int ModFrom = 7;
    private const int ModTo = 8;

    private const string Examples =
        "1850, 1850-03, 1850-1860, between 1850-03 and 1851, from 1850, to 1900, before 1900-05-01, after 1850, " +
        "1856-07-20 (Julian)";

    /// <summary>A Gramps Web <c>y/m/d</c> date, rewritten as ISO before parsing.</summary>
    private static readonly Regex SlashYmd = new(
        @"(?<![\d/.])(?<y>\d{1,4})/(?<m>\d{1,2})/(?<d>\d{1,2})(?![\d/.])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>A day- or month-first date left after the <c>y/m/d</c> dates are rewritten.</summary>
    private static readonly Regex NumericTriplet = new(
        @"\d+[/.]\d+[/.]\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Returns <c>null</c> without a filter; throws a validation error for dates it can't bound.</summary>
    public static TimelineDates? Parse(string? dates)
    {
        if (string.IsNullOrWhiteSpace(dates))
            return null;

        var raw = dates.Trim();
        var iso = SlashYmd.Replace(raw, m => $"{Pad(m, "y", 4)}-{Pad(m, "m", 2)}-{Pad(m, "d", 2)}");
        if (NumericTriplet.IsMatch(iso))
            throw McpToolErrors.ValidationError(
                $"Timeline dates \"{raw}\" put the day or month first. Put the year first: 1850-03-01 or 1850/3/1.");

        var date = AgentDateParser.ToDateRequestOrNull(iso, DateComponentOrder.Iso, DateIntervalPreference.Span)!;
        if (date.Quality != 0 || date.Modifier is not (0 or ModBefore or ModAfter or ModRange or ModSpan or ModFrom or ModTo))
            throw McpToolErrors.ValidationError(
                $"Timeline dates \"{raw}\" are approximate, and the filter needs exact bounds. Use a year, month, day, " +
                $"or range: {Examples}.");

        var interval = date.Modifier is ModRange or ModSpan;
        if (date.NewYear != 0 && (date.Day == 0 || (interval && date.EndDay == 0)))
            throw McpToolErrors.ValidationError(
                $"Timeline dates \"{raw}\" give the new year with a year or month alone. Give the full date, or leave " +
                "out the new year and count the year from 1 January.");

        var first = First(date, date.Year, date.Month, date.Day);
        var last = interval
            ? Last(date, date.EndYear, date.EndMonth, date.EndDay)
            : Last(date, date.Year, date.Month, date.Day);
        var range = date.Modifier switch
        {
            ModFrom => new TimelineSdnRange(first, null),
            ModAfter => new TimelineSdnRange(last + 1, null),
            ModTo => new TimelineSdnRange(null, last),
            ModBefore => new TimelineSdnRange(null, first - 1),
            _ => new TimelineSdnRange(first, last)
        };

        return new TimelineDates($"{GregorianYmd(raw, range.MinInclusive)}-{GregorianYmd(raw, range.MaxInclusive)}", range);
    }

    private static string Pad(Match match, string group, int width) =>
        match.Groups[group].Value.PadLeft(width, '0');

    /// <summary>The first day a year, month, or day covers.</summary>
    private static int First(DateRequest date, int year, int month, int day)
    {
        if (day != 0)
            return GrampsDateSortVal.SortValueOf(date.Calendar, date.NewYear, year, month, day);
        return GrampsCalendars.ToSdn(date.Calendar, year, Math.Max(month, 1), 1);
    }

    /// <summary>The last day a year, month, or day covers.</summary>
    private static int Last(DateRequest date, int year, int month, int day)
    {
        if (day != 0)
            return GrampsDateSortVal.SortValueOf(date.Calendar, date.NewYear, year, month, day);
        if (month == 0)
            return GrampsCalendars.ToSdn(date.Calendar, year + 1, 1, 1) - 1;

        // Months differ in length between calendars, and Hebrew ones between years too.
        var sdn = GrampsCalendars.ToSdn(date.Calendar, year, month, 1);
        while (GrampsCalendars.FromSdn(date.Calendar, sdn + 1) is var next && next.Year == year && next.Month == month)
            sdn++;
        return sdn;
    }

    private static string GregorianYmd(string raw, int? sdn)
    {
        if (sdn is not { } value)
            return "";

        var (year, month, day) = GrampsCalendars.FromSdn(GrampsCalendars.Gregorian, value);
        if (year < 1)
            throw McpToolErrors.ValidationError($"Timeline dates \"{raw}\" reach before year 1, which Gramps Web can't filter by.");
        return string.Create(CultureInfo.InvariantCulture, $"{year}/{month}/{day}");
    }
}

/// <summary>A get_timeline date filter: the <c>dates</c> query for Gramps Web, and the same bounds as serial days.</summary>
internal sealed record TimelineDates(string ApiDates, TimelineSdnRange Range);

/// <summary>Inclusive SDN range; null bound means open.</summary>
internal readonly record struct TimelineSdnRange(int? MinInclusive, int? MaxInclusive)
{
    public bool Contains(int sortKey)
    {
        if (MinInclusive is { } lo && sortKey < lo)
            return false;
        if (MaxInclusive is { } hi && sortKey > hi)
            return false;
        return true;
    }
}
