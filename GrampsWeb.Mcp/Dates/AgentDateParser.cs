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

    private static readonly Regex BetweenYears = new(
        @"^between\s+(?<a>\d{1,4})\s+and\s+(?<b>\d{1,4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex FromToYears = new(
        @"^from\s+(?<a>\d{1,4})\s+to\s+(?<b>\d{1,4})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

        var betweenMatch = BetweenYears.Match(working);
        if (betweenMatch.Success)
        {
            var y1 = int.Parse(betweenMatch.Groups["a"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(betweenMatch.Groups["b"].Value, CultureInfo.InvariantCulture);
            return RangeYears(y1, y2);
        }

        var fromToMatch = FromToYears.Match(working);
        if (fromToMatch.Success)
        {
            var y1 = int.Parse(fromToMatch.Groups["a"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(fromToMatch.Groups["b"].Value, CultureInfo.InvariantCulture);
            return SpanYears(y1, y2);
        }

        var dash = YearDashYear.Match(working);
        if (dash.Success)
        {
            var y1 = int.Parse(dash.Groups["a"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(dash.Groups["b"].Value, CultureInfo.InvariantCulture);
            return RangeYears(y1, y2);
        }

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

    private static DateRequest RangeYears(int y1, int y2) => new()
    {
        Calendar = 0,
        Modifier = 4,
        Quality = 0,
        Year = y1,
        EndYear = y2
    };

    private static DateRequest SpanYears(int y1, int y2) => new()
    {
        Calendar = 0,
        Modifier = 5,
        Quality = 0,
        Year = y1,
        EndYear = y2
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
