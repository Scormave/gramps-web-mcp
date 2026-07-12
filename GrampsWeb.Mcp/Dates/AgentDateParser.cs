using System.Globalization;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;

namespace GrampsWeb.Mcp.Dates;

/// <summary>
/// Parses human-readable date strings from MCP tools into <see cref="DateRequest"/> (Gramps API shape).
/// </summary>
public static class AgentDateParser
{
    private static readonly Regex IsoFull = new(
        @"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoMonthYear = new(
        @"^(?<y>\d{4})-(?<m>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoYear = new(
        @"^\d{4}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Year–year only (both parts look like years, not yyyy-mm).</summary>
    private static readonly Regex YearDashYear = new(
        @"^(?<a>\d{3,4})\s*[-–]\s*(?<b>\d{3,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoFullDashRange = new(
        @"^(?<y1>\d{4})-(?<m1>\d{1,2})-(?<d1>\d{1,2})\s*[-–]\s*(?<y2>\d{4})-(?<m2>\d{1,2})-(?<d2>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoMonthDashRange = new(
        @"^(?<y1>\d{4})-(?<m1>\d{1,2})\s*[-–]\s*(?<y2>\d{4})-(?<m2>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BetweenParts = new(
        @"^between\s+(?<a>.+?)\s+and\s+(?<b>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FromToParts = new(
        @"^from\s+(?<a>.+?)\s+to\s+(?<b>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OpenEndedYearAfter = new(
        @"^(?<y>\d{3,4})\s*[-–]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OpenEndedYearBefore = new(
        @"^[-–]\s*(?<y>\d{3,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OpenEndedIsoAfter = new(
        @"^(?<y>\d{4})-(?<m>\d{1,2})(?:-(?<d>\d{1,2}))?\s*[-–]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OpenEndedIsoBefore = new(
        @"^[-–]\s*(?<y>\d{4})-(?<m>\d{1,2})(?:-(?<d>\d{1,2}))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumericTriplet = new(
        @"^(?<p1>\d{1,4})[-/.](?<p2>\d{1,4})[-/.](?<p3>\d{1,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns <c>null</c> for null/whitespace input. Otherwise parses or uses text-only fallback.
    /// Throws <see cref="McpException"/> when <paramref name="order"/> is <see cref="DateComponentOrder.Iso"/>
    /// but the value looks like a day/month/year triplet that is not ISO-8601.
    /// </summary>
    public static DateRequest? ToDateRequestOrNull(string? input, DateComponentOrder order = DateComponentOrder.Iso)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var raw = input.Trim();
        var (working, modifier) = StripModifierPrefix(raw);

        var betweenMatch = BetweenParts.Match(working);
        if (betweenMatch.Success
            && TryParseSingleCalendarSide(betweenMatch.Groups["a"].Value.Trim(), out var betweenStart)
            && TryParseSingleCalendarSide(betweenMatch.Groups["b"].Value.Trim(), out var betweenEnd))
        {
            return RangeCalendar(betweenStart, betweenEnd);
        }

        var fromToMatch = FromToParts.Match(working);
        if (fromToMatch.Success
            && TryParseSingleCalendarSide(fromToMatch.Groups["a"].Value.Trim(), out var spanStart)
            && TryParseSingleCalendarSide(fromToMatch.Groups["b"].Value.Trim(), out var spanEnd))
        {
            return SpanCalendar(spanStart, spanEnd);
        }

        var isoFullRange = IsoFullDashRange.Match(working);
        if (isoFullRange.Success)
        {
            var d1 = int.Parse(isoFullRange.Groups["d1"].Value, CultureInfo.InvariantCulture);
            var m1 = int.Parse(isoFullRange.Groups["m1"].Value, CultureInfo.InvariantCulture);
            var y1 = int.Parse(isoFullRange.Groups["y1"].Value, CultureInfo.InvariantCulture);
            var d2 = int.Parse(isoFullRange.Groups["d2"].Value, CultureInfo.InvariantCulture);
            var m2 = int.Parse(isoFullRange.Groups["m2"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(isoFullRange.Groups["y2"].Value, CultureInfo.InvariantCulture);
            ValidateDayMonth(d1, m1);
            ValidateDayMonth(d2, m2);
            return RangeCalendar(
                new CalendarSide(d1, m1, y1),
                new CalendarSide(d2, m2, y2));
        }

        var isoMonthRange = IsoMonthDashRange.Match(working);
        if (isoMonthRange.Success)
        {
            var m1 = int.Parse(isoMonthRange.Groups["m1"].Value, CultureInfo.InvariantCulture);
            var y1 = int.Parse(isoMonthRange.Groups["y1"].Value, CultureInfo.InvariantCulture);
            var m2 = int.Parse(isoMonthRange.Groups["m2"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(isoMonthRange.Groups["y2"].Value, CultureInfo.InvariantCulture);
            if (m1 is < 1 or > 12 || m2 is < 1 or > 12)
                throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
            return RangeCalendar(
                new CalendarSide(0, m1, y1),
                new CalendarSide(0, m2, y2));
        }

        var dash = YearDashYear.Match(working);
        if (dash.Success)
        {
            var y1 = int.Parse(dash.Groups["a"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(dash.Groups["b"].Value, CultureInfo.InvariantCulture);
            return RangeYears(y1, y2);
        }

        if (TryParseOpenEnded(working, out var openEnded))
            return openEnded;

        if (TryParseIso(working, modifier, out var iso))
            return iso;

        var trip = NumericTriplet.Match(working);
        if (trip.Success)
        {
            if (order == DateComponentOrder.Iso)
                throw McpToolErrors.ValidationError(
                    "Date uses slashes or dots in day/month/year form. Pass dateComponentOrder=DayMonthYear or MonthDayYear, or use ISO yyyy-MM-dd.");

            var p1 = int.Parse(trip.Groups["p1"].Value, CultureInfo.InvariantCulture);
            var p2 = int.Parse(trip.Groups["p2"].Value, CultureInfo.InvariantCulture);
            var p3 = int.Parse(trip.Groups["p3"].Value, CultureInfo.InvariantCulture);

            int day, month, year;
            if (order == DateComponentOrder.DayMonthYear)
            {
                day = p1;
                month = p2;
                year = NormalizeYear(p3);
            }
            else
            {
                month = p1;
                day = p2;
                year = NormalizeYear(p3);
            }

            ValidateDayMonth(day, month);
            return SingleCalendarDate(modifier, day, month, year);
        }

        return TextOnlyDate(raw);
    }

    private static bool TryParseOpenEnded(string working, out DateRequest? req)
    {
        req = null;

        var isoAfter = OpenEndedIsoAfter.Match(working);
        if (isoAfter.Success)
        {
            var y = int.Parse(isoAfter.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(isoAfter.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = isoAfter.Groups["d"].Success
                ? int.Parse(isoAfter.Groups["d"].Value, CultureInfo.InvariantCulture)
                : 0;
            if (m is < 1 or > 12)
                throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
            if (d > 0)
                ValidateDayMonth(d, m);
            req = d > 0
                ? SingleCalendarDate(2, d, m, y)
                : new DateRequest { Calendar = 0, Modifier = 2, Quality = 0, Month = m, Year = y };
            return true;
        }

        var isoBefore = OpenEndedIsoBefore.Match(working);
        if (isoBefore.Success)
        {
            var y = int.Parse(isoBefore.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(isoBefore.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = isoBefore.Groups["d"].Success
                ? int.Parse(isoBefore.Groups["d"].Value, CultureInfo.InvariantCulture)
                : 0;
            if (m is < 1 or > 12)
                throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
            if (d > 0)
                ValidateDayMonth(d, m);
            req = d > 0
                ? SingleCalendarDate(1, d, m, y)
                : new DateRequest { Calendar = 0, Modifier = 1, Quality = 0, Month = m, Year = y };
            return true;
        }

        var yearAfter = OpenEndedYearAfter.Match(working);
        if (yearAfter.Success)
        {
            var y = int.Parse(yearAfter.Groups["y"].Value, CultureInfo.InvariantCulture);
            req = YearDate(2, y);
            return true;
        }

        var yearBefore = OpenEndedYearBefore.Match(working);
        if (yearBefore.Success)
        {
            var y = int.Parse(yearBefore.Groups["y"].Value, CultureInfo.InvariantCulture);
            req = YearDate(1, y);
            return true;
        }

        return false;
    }

    private static bool TryParseSingleCalendarSide(string side, out CalendarSide result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(side))
            return false;

        var full = IsoFull.Match(side);
        if (full.Success)
        {
            var y = int.Parse(full.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(full.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(full.Groups["d"].Value, CultureInfo.InvariantCulture);
            ValidateDayMonth(d, m);
            result = new CalendarSide(d, m, y);
            return true;
        }

        var monthYear = IsoMonthYear.Match(side);
        if (monthYear.Success)
        {
            var y = int.Parse(monthYear.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(monthYear.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (m is < 1 or > 12)
                throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
            result = new CalendarSide(0, m, y);
            return true;
        }

        if (IsoYear.IsMatch(side) || Regex.IsMatch(side, @"^\d{3,4}$", RegexOptions.CultureInvariant))
        {
            var y = int.Parse(side, CultureInfo.InvariantCulture);
            result = new CalendarSide(0, 0, y);
            return true;
        }

        return false;
    }

    private static (string working, int modifier) StripModifierPrefix(string raw)
    {
        var lower = raw;
        if (lower.StartsWith("before ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(7).Trim(), 1);
        if (lower.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), 2);
        if (lower.StartsWith("about ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), 3);
        if (lower.StartsWith("circa ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), 3);
        return (raw, 0);
    }

    private static bool TryParseIso(string working, int modifier, out DateRequest? req)
    {
        req = null;
        var m = IsoFull.Match(working);
        if (m.Success)
        {
            var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            ValidateDayMonth(d, mo);
            req = SingleCalendarDate(modifier, d, mo, y);
            return true;
        }

        m = IsoMonthYear.Match(working);
        if (m.Success)
        {
            var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (mo is < 1 or > 12)
                throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
            req = new DateRequest
            {
                Calendar = 0,
                Modifier = modifier,
                Quality = 0,
                Month = mo,
                Year = y
            };
            return true;
        }

        if (IsoYear.IsMatch(working))
        {
            var y = int.Parse(working, CultureInfo.InvariantCulture);
            req = YearDate(modifier, y);
            return true;
        }

        return false;
    }

    private readonly record struct CalendarSide(int Day, int Month, int Year);

    private static DateRequest YearDate(int modifier, int year) => new()
    {
        Calendar = 0,
        Modifier = modifier,
        Quality = 0,
        Year = year
    };

    private static DateRequest SingleCalendarDate(int modifier, int day, int month, int year) => new()
    {
        Calendar = 0,
        Modifier = modifier,
        Quality = 0,
        Day = day,
        Month = month,
        Year = year,
        Slash = false
    };

    private static DateRequest RangeYears(int y1, int y2) =>
        RangeCalendar(new CalendarSide(0, 0, y1), new CalendarSide(0, 0, y2));

    private static DateRequest RangeCalendar(CalendarSide start, CalendarSide end) => new()
    {
        Calendar = 0,
        Modifier = 4,
        Quality = 0,
        Day = start.Day,
        Month = start.Month,
        Year = start.Year,
        EndDay = end.Day,
        EndMonth = end.Month,
        EndYear = end.Year
    };

    private static DateRequest SpanCalendar(CalendarSide start, CalendarSide end) => new()
    {
        Calendar = 0,
        Modifier = 5,
        Quality = 0,
        Day = start.Day,
        Month = start.Month,
        Year = start.Year,
        EndDay = end.Day,
        EndMonth = end.Month,
        EndYear = end.Year
    };

    private static DateRequest TextOnlyDate(string text) => new()
    {
        Calendar = 0,
        Modifier = 6,
        Quality = 0,
        Text = text
    };

    private static void ValidateDayMonth(int day, int month)
    {
        if (month is < 1 or > 12)
            throw McpToolErrors.ValidationError("Invalid month in date (use 1–12).");
        if (day is < 1 or > 31)
            throw McpToolErrors.ValidationError("Invalid day in date.");
    }

    private static int NormalizeYear(int y) => y < 100 ? (y >= 70 ? 1900 + y : 2000 + y) : y;
}
