using System.Diagnostics.CodeAnalysis;
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
    private const int ModBefore = 1;
    private const int ModAfter = 2;
    private const int ModRange = 4;
    private const int ModSpan = 5;
    private const int ModFrom = 7;
    private const int ModTo = 8;

    private const string UnrecognizedDateGuidance =
        "Use ISO (yyyy-MM-dd / yyyy-MM / yyyy), English months (1 Jul 1919 or July 1919), " +
        "ranges (1800-1850, from … to …, between … and …), or prefixes (before/after/about, estimated/calculated). " +
        "See get_reference(topic: \"input-guide\", section: \"dates\").";

    private static readonly Regex IsoFull = new(
        @"^(?<y>\d{3,4})-(?<m>\d{1,2})-(?<d>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoMonthYear = new(
        @"^(?<y>\d{3,4})-(?<m>\d{1,2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IsoYear = new(
        @"^\d{3,4}$",
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

    private static readonly Regex FromOnly = new(
        @"^from\s+(?<a>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ToOnly = new(
        @"^to\s+(?<a>.+)$",
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

    private static readonly Regex OpenEndedTrailingDash = new(
        @"^(?<side>.+?)\s*[-–]\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OpenEndedLeadingDash = new(
        @"^[-–]\s*(?<side>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NumericTriplet = new(
        @"^(?<p1>\d{1,4})[-/.](?<p2>\d{1,4})[-/.](?<p3>\d{1,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The calendar and new-year day Gramps appends to a date outside the Gregorian calendar or the January
    /// year: <c>1856-07-20 (Julian)</c>, <c>1735-03-10 (Julian, Mar25)</c>, <c>1735-03-10 (Mar25)</c>.
    /// </summary>
    private static readonly Regex CalendarSuffix = new(
        @"\s*\(\s*(?<first>[^(),]*?)\s*(?:,\s*(?<second>[^(),]*?)\s*)?\)\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// A year of one or two digits in a French Republican date (<c>12-03-15</c>, <c>an 12</c> as <c>12</c>), not a
    /// month or day after a dash and not part of a slash or dot triplet.
    /// </summary>
    private static readonly Regex ShortYear = new(
        @"(?<![\d/.])(?<!\d-)(?<y>\d{1,2})(?![\d/.])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Word = new(
        @"[A-Za-z]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>New-year names by Gramps code, as Gramps writes them after the calendar.</summary>
    private static readonly string[] NewYearNames = ["Jan1", "Mar1", "Mar25", "Sep1"];

    /// <summary>
    /// Day + English month + year, optional comma before year: <c>1 Jul 1919</c>, <c>5 July, 1944</c>.
    /// </summary>
    private static readonly Regex EnglishDayMonthYear = new(
        @"^(?<d>\d{1,2})\s+(?<mon>[A-Za-z]+),?\s+(?<y>\d{3,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>English month + year: <c>Jul 1919</c>, <c>October 1929</c>.</summary>
    private static readonly Regex EnglishMonthYear = new(
        @"^(?<mon>[A-Za-z]+)\s+(?<y>\d{3,4})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns <c>null</c> for null/whitespace input. Otherwise parses a structured date.
    /// Throws <see cref="McpException"/> when the value cannot be parsed, or when
    /// <paramref name="order"/> is <see cref="DateComponentOrder.Iso"/> but the value looks
    /// like a day/month/year triplet that is not ISO-8601.
    /// </summary>
    public static DateRequest? ToDateRequestOrNull(
        string? input,
        DateComponentOrder order = DateComponentOrder.Iso,
        DateIntervalPreference intervalPreference = DateIntervalPreference.Span)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var raw = input.Trim();
        var (dated, calendar, newYear) = StripCalendarSuffix(raw);
        if (dated.Contains('('))
            throw McpToolErrors.ValidationError(
                $"Date \"{raw}\" has parentheses inside the date. Put the calendar once at the end: " +
                "between 1850-01-01 and 1860-01-01 (Julian).");
        if (!GrampsCalendars.HasEnglishMonthNames(calendar))
            RejectEnglishMonths(raw, dated, calendar);
        if (calendar == GrampsCalendars.FrenchRepublican && order == DateComponentOrder.Iso)
            dated = ShortYear.Replace(dated, m => m.Groups["y"].Value.PadLeft(3, '0'));

        var (unqualified, quality) = StripQualityPrefix(dated);
        var request = ParseUnqualified(raw, unqualified, order, intervalPreference, calendar);
        request.Quality = quality;
        request.Calendar = calendar;
        request.NewYear = newYear;
        ValidateInCalendar(raw, dated, request);
        return request;
    }

    /// <summary>
    /// Splits the trailing <c>(Julian)</c>, <c>(Julian, Mar25)</c>, or <c>(Mar25)</c> off a date, as the Gramps date
    /// parser does. Without one the date is Gregorian with the year starting on 1 January.
    /// </summary>
    private static (string Dated, int Calendar, int NewYear) StripCalendarSuffix(string raw)
    {
        var match = CalendarSuffix.Match(raw);
        if (!match.Success)
            return (raw, GrampsCalendars.Gregorian, 0);

        var first = match.Groups["first"].Value;
        var second = match.Groups["second"].Success ? match.Groups["second"].Value : null;
        int calendar;
        int newYear;
        if (second == null && IndexOf(NewYearNames, first) is >= 0 and var onlyNewYear)
        {
            calendar = GrampsCalendars.Gregorian;
            newYear = onlyNewYear;
        }
        else
        {
            calendar = IndexOf(GrampsCalendars.Names, first);
            newYear = second == null ? 0 : IndexOf(NewYearNames, second);
            if (calendar < 0 || newYear < 0)
                throw McpToolErrors.ValidationError(
                    $"Date \"{raw}\" ends in \"{match.Value.Trim()}\", which isn't a Gramps calendar or new year. " +
                    $"Use {string.Join(", ", GrampsCalendars.Names)}, optionally followed by the day the year " +
                    "starts (Mar1, Mar25, Sep1): 1856-07-20 (Julian), 1735-03-10 (Julian, Mar25).");
        }

        if (newYear != 0 && GrampsCalendars.HasFixedNewYear(calendar))
            throw McpToolErrors.ValidationError(
                $"Date \"{raw}\": the {GrampsCalendars.Names[calendar]} calendar has its own new year. A new year " +
                "such as Mar25 can only follow a Gregorian, Julian, or Swedish date.");

        return (raw[..match.Index].TrimEnd(), calendar, newYear);
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>English month names only exist in the Gregorian, Julian, and Swedish calendars.</summary>
    private static void RejectEnglishMonths(string raw, string dated, int calendar)
    {
        foreach (Match word in Word.Matches(dated))
        {
            if (EnglishMonthNames.TryParse(word.Value, out _))
                throw McpToolErrors.ValidationError(
                    $"Date \"{raw}\" names the month \"{word.Value}\", but the {GrampsCalendars.Names[calendar]} " +
                    $"calendar has its own months. Write the month as a number (1–{GrampsCalendars.MonthsInYear(calendar)}): " +
                    $"{ExampleIn(calendar)}. See get_reference(topic: \"input-guide\", section: \"dates\").");
        }
    }

    private static string ExampleIn(int calendar) => calendar switch
    {
        GrampsCalendars.Hebrew => "5600-01-15 (Hebrew)",
        GrampsCalendars.FrenchRepublican => "12-03-15 (French Republican)",
        GrampsCalendars.Persian => "1300-01-15 (Persian)",
        GrampsCalendars.Islamic => "1250-01-15 (Islamic)",
        _ => $"1856-07-20 ({GrampsCalendars.Names[calendar]})"
    };

    /// <summary>
    /// Rejects dates Gramps would refuse to store: a day the month doesn't have in the date's calendar
    /// (<c>1900-02-29</c> in the Gregorian calendar, Adar II in a common Hebrew year), or a range Gramps can't
    /// combine with the new year.
    /// </summary>
    private static void ValidateInCalendar(string raw, string dated, DateRequest request)
    {
        if (!ExistsInCalendar(request.Calendar, request))
        {
            var hint = request.Calendar switch
            {
                GrampsCalendars.Gregorian when ExistsInCalendar(GrampsCalendars.Julian, request) =>
                    $" For an Old Style date add the calendar: {dated} (Julian).",
                GrampsCalendars.Hebrew when request.Month == 7 || request.EndMonth == 7 =>
                    " Adar II (month 7) exists only in leap years; Adar in other years is month 6.",
                _ => ""
            };
            throw McpToolErrors.ValidationError(
                $"Date \"{raw}\" doesn't exist in the {GrampsCalendars.Names[request.Calendar]} calendar.{hint}");
        }

        if (!GrampsDateSortVal.PassesGrampsDateCheck(request))
            throw McpToolErrors.ValidationError(
                $"Date \"{raw}\": Gramps can't store a range or span with the new year {NewYearNames[request.NewYear]} " +
                "when it starts on or after that day. Leave out the new year and count both years from 1 January.");
    }

    /// <summary>The start date, and the end date of a range or span, exist in the calendar.</summary>
    private static bool ExistsInCalendar(int calendar, DateRequest request) =>
        GrampsDateSortVal.PassesGrampsDateCheck(new DateRequest
        {
            Calendar = calendar,
            Modifier = request.Modifier is ModRange or ModSpan ? request.Modifier : 0,
            Year = request.Year,
            Month = request.Month,
            Day = request.Day,
            EndYear = request.EndYear,
            EndMonth = request.EndMonth,
            EndDay = request.EndDay
        });

    private static DateRequest ParseUnqualified(
        string raw,
        string unqualified,
        DateComponentOrder order,
        DateIntervalPreference intervalPreference,
        int calendar)
    {
        var (working, modifier) = StripModifierPrefix(unqualified);

        var betweenMatch = BetweenParts.Match(working);
        if (betweenMatch.Success
            && TryParseSingleCalendarSide(betweenMatch.Groups["a"].Value.Trim(), calendar, out var betweenStart)
            && TryParseSingleCalendarSide(betweenMatch.Groups["b"].Value.Trim(), calendar, out var betweenEnd))
        {
            return IntervalCalendar(betweenStart, betweenEnd, DateIntervalPreference.Range);
        }

        var fromToMatch = FromToParts.Match(working);
        if (fromToMatch.Success
            && TryParseSingleCalendarSide(fromToMatch.Groups["a"].Value.Trim(), calendar, out var spanStart)
            && TryParseSingleCalendarSide(fromToMatch.Groups["b"].Value.Trim(), calendar, out var spanEnd))
        {
            return IntervalCalendar(spanStart, spanEnd, DateIntervalPreference.Span);
        }

        var fromOnly = FromOnly.Match(working);
        if (fromOnly.Success
            && TryParseSingleCalendarSide(fromOnly.Groups["a"].Value.Trim(), calendar, out var fromSide))
        {
            return CalendarSideDate(ModFrom, fromSide);
        }

        var toOnly = ToOnly.Match(working);
        if (toOnly.Success
            && TryParseSingleCalendarSide(toOnly.Groups["a"].Value.Trim(), calendar, out var toSide))
        {
            return CalendarSideDate(ModTo, toSide);
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
            ValidateDayMonth(d1, m1, calendar);
            ValidateDayMonth(d2, m2, calendar);
            return IntervalCalendar(
                new CalendarSide(d1, m1, y1),
                new CalendarSide(d2, m2, y2),
                intervalPreference);
        }

        var isoMonthRange = IsoMonthDashRange.Match(working);
        if (isoMonthRange.Success)
        {
            var m1 = int.Parse(isoMonthRange.Groups["m1"].Value, CultureInfo.InvariantCulture);
            var y1 = int.Parse(isoMonthRange.Groups["y1"].Value, CultureInfo.InvariantCulture);
            var m2 = int.Parse(isoMonthRange.Groups["m2"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(isoMonthRange.Groups["y2"].Value, CultureInfo.InvariantCulture);
            ValidateMonth(m1, calendar);
            ValidateMonth(m2, calendar);
            return IntervalCalendar(
                new CalendarSide(0, m1, y1),
                new CalendarSide(0, m2, y2),
                intervalPreference);
        }

        var dash = YearDashYear.Match(working);
        if (dash.Success)
        {
            var y1 = int.Parse(dash.Groups["a"].Value, CultureInfo.InvariantCulture);
            var y2 = int.Parse(dash.Groups["b"].Value, CultureInfo.InvariantCulture);
            return IntervalCalendar(
                new CalendarSide(0, 0, y1),
                new CalendarSide(0, 0, y2),
                intervalPreference);
        }

        if (TryParseMixedPrecisionDash(working, intervalPreference, calendar, out var mixed))
            return mixed;

        if (TryParseOpenEnded(working, intervalPreference, calendar, out var openEnded))
            return openEnded;

        if (TryParseIso(working, modifier, calendar, out var iso))
            return iso;

        if (TryParseEnglish(working, out var englishSide))
            return CalendarSideDate(modifier, englishSide);

        var trip = NumericTriplet.Match(working);
        if (trip.Success)
        {
            if (order == DateComponentOrder.Iso)
                throw McpToolErrors.ValidationError(
                    "Date uses slashes or dots in day/month/year form. Pass dateComponentOrder=DayMonthYear or MonthDayYear, or use ISO yyyy-MM-dd.");

            var p1 = int.Parse(trip.Groups["p1"].Value, CultureInfo.InvariantCulture);
            var p2 = int.Parse(trip.Groups["p2"].Value, CultureInfo.InvariantCulture);
            var p3 = int.Parse(trip.Groups["p3"].Value, CultureInfo.InvariantCulture);

            var (day, month) = order == DateComponentOrder.DayMonthYear ? (p1, p2) : (p2, p1);
            var year = NormalizeYear(p3, calendar, raw);

            ValidateDayMonth(day, month, calendar);
            return SingleCalendarDate(modifier, day, month, year);
        }

        throw McpToolErrors.ValidationError($"Unrecognized date \"{raw}\". {UnrecognizedDateGuidance}");
    }

    private static bool TryParseMixedPrecisionDash(
        string working,
        DateIntervalPreference preference,
        int calendar,
        [NotNullWhen(true)] out DateRequest? req)
    {
        req = null;
        for (var i = 1; i < working.Length - 1; i++)
        {
            var c = working[i];
            if (c is not ('-' or '–'))
                continue;

            var left = working[..i].Trim();
            var right = working[(i + 1)..].Trim();
            if (left.Length == 0 || right.Length == 0)
                continue;

            if (!TryParseSingleCalendarSide(left, calendar, out var start))
                continue;
            if (!TryParseSingleCalendarSide(right, calendar, out var end))
                continue;

            req = IntervalCalendar(start, end, preference);
            return true;
        }

        return false;
    }

    private static bool TryParseOpenEnded(
        string working,
        DateIntervalPreference preference,
        int calendar,
        [NotNullWhen(true)] out DateRequest? req)
    {
        req = null;
        var openStartMod = preference == DateIntervalPreference.Range ? ModAfter : ModFrom;
        var openEndMod = preference == DateIntervalPreference.Range ? ModBefore : ModTo;

        var isoAfter = OpenEndedIsoAfter.Match(working);
        if (isoAfter.Success)
        {
            var y = int.Parse(isoAfter.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(isoAfter.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = isoAfter.Groups["d"].Success
                ? int.Parse(isoAfter.Groups["d"].Value, CultureInfo.InvariantCulture)
                : 0;
            ValidateMonth(m, calendar);
            if (d > 0)
                ValidateDayMonth(d, m, calendar);
            req = d > 0
                ? SingleCalendarDate(openStartMod, d, m, y)
                : new DateRequest { Modifier = openStartMod, Quality = 0, Month = m, Year = y };
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
            ValidateMonth(m, calendar);
            if (d > 0)
                ValidateDayMonth(d, m, calendar);
            req = d > 0
                ? SingleCalendarDate(openEndMod, d, m, y)
                : new DateRequest { Modifier = openEndMod, Quality = 0, Month = m, Year = y };
            return true;
        }

        var yearAfter = OpenEndedYearAfter.Match(working);
        if (yearAfter.Success)
        {
            var y = int.Parse(yearAfter.Groups["y"].Value, CultureInfo.InvariantCulture);
            req = YearDate(openStartMod, y);
            return true;
        }

        var yearBefore = OpenEndedYearBefore.Match(working);
        if (yearBefore.Success)
        {
            var y = int.Parse(yearBefore.Groups["y"].Value, CultureInfo.InvariantCulture);
            req = YearDate(openEndMod, y);
            return true;
        }

        var trailing = OpenEndedTrailingDash.Match(working);
        if (trailing.Success
            && TryParseSingleCalendarSide(trailing.Groups["side"].Value.Trim(), calendar, out var afterSide))
        {
            req = CalendarSideDate(openStartMod, afterSide);
            return true;
        }

        var leading = OpenEndedLeadingDash.Match(working);
        if (leading.Success
            && TryParseSingleCalendarSide(leading.Groups["side"].Value.Trim(), calendar, out var beforeSide))
        {
            req = CalendarSideDate(openEndMod, beforeSide);
            return true;
        }

        return false;
    }

    private static bool TryParseSingleCalendarSide(string side, int calendar, out CalendarSide result)
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
            ValidateDayMonth(d, m, calendar);
            result = new CalendarSide(d, m, y);
            return true;
        }

        var monthYear = IsoMonthYear.Match(side);
        if (monthYear.Success)
        {
            var y = int.Parse(monthYear.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(monthYear.Groups["m"].Value, CultureInfo.InvariantCulture);
            ValidateMonth(m, calendar);
            result = new CalendarSide(0, m, y);
            return true;
        }

        if (IsoYear.IsMatch(side))
        {
            var y = int.Parse(side, CultureInfo.InvariantCulture);
            result = new CalendarSide(0, 0, y);
            return true;
        }

        return TryParseEnglish(side, out result);
    }

    private static bool TryParseEnglish(string side, out CalendarSide result)
    {
        result = default;

        var dmy = EnglishDayMonthYear.Match(side);
        if (dmy.Success
            && EnglishMonthNames.TryParse(dmy.Groups["mon"].Value, out var month)
            && int.TryParse(dmy.Groups["d"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            && int.TryParse(dmy.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            ValidateDay(day);
            result = new CalendarSide(day, month, year);
            return true;
        }

        var my = EnglishMonthYear.Match(side);
        if (my.Success
            && EnglishMonthNames.TryParse(my.Groups["mon"].Value, out var mon)
            && int.TryParse(my.Groups["y"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var y))
        {
            result = new CalendarSide(0, mon, y);
            return true;
        }

        return false;
    }

    /// <summary>Strips the quality Gramps writes before a date: <c>estimated about 1930</c>.</summary>
    private static (string working, int quality) StripQualityPrefix(string raw)
    {
        if (raw.StartsWith("estimated ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(10).Trim(), 1);
        if (raw.StartsWith("calculated ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(11).Trim(), 2);
        return (raw, 0);
    }

    private static (string working, int modifier) StripModifierPrefix(string raw)
    {
        var lower = raw;
        if (lower.StartsWith("before ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(7).Trim(), ModBefore);
        if (lower.StartsWith("after ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), ModAfter);
        if (lower.StartsWith("about ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), 3);
        if (lower.StartsWith("circa ", StringComparison.OrdinalIgnoreCase))
            return (raw.Substring(6).Trim(), 3);
        return (raw, 0);
    }

    private static bool TryParseIso(string working, int modifier, int calendar, [NotNullWhen(true)] out DateRequest? req)
    {
        req = null;
        var m = IsoFull.Match(working);
        if (m.Success)
        {
            var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
            ValidateDayMonth(d, mo, calendar);
            req = SingleCalendarDate(modifier, d, mo, y);
            return true;
        }

        m = IsoMonthYear.Match(working);
        if (m.Success)
        {
            var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
            var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            ValidateMonth(mo, calendar);
            req = new DateRequest
            {
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
        Modifier = modifier,
        Quality = 0,
        Year = year
    };

    private static DateRequest SingleCalendarDate(int modifier, int day, int month, int year) => new()
    {
        Modifier = modifier,
        Quality = 0,
        Day = day,
        Month = month,
        Year = year,
        Slash = false
    };

    private static DateRequest CalendarSideDate(int modifier, CalendarSide side) => new()
    {
        Modifier = modifier,
        Quality = 0,
        Day = side.Day,
        Month = side.Month,
        Year = side.Year
    };

    private static DateRequest IntervalCalendar(
        CalendarSide start,
        CalendarSide end,
        DateIntervalPreference preference) => new()
    {
        Modifier = preference == DateIntervalPreference.Range ? ModRange : ModSpan,
        Quality = 0,
        Day = start.Day,
        Month = start.Month,
        Year = start.Year,
        EndDay = end.Day,
        EndMonth = end.Month,
        EndYear = end.Year
    };

    private static void ValidateDayMonth(int day, int month, int calendar)
    {
        ValidateMonth(month, calendar);
        ValidateDay(day);
    }

    private static void ValidateMonth(int month, int calendar)
    {
        var months = GrampsCalendars.MonthsInYear(calendar);
        if (month < 1 || month > months)
            throw McpToolErrors.ValidationError($"Invalid month in date (use 1–{months}).");
    }

    private static void ValidateDay(int day)
    {
        if (day is < 1 or > 31)
            throw McpToolErrors.ValidationError("Invalid day in date.");
    }

    /// <summary>
    /// Reads a two-digit Gregorian year as 1970–2069. French Republican years are short; other calendars need the
    /// full year.
    /// </summary>
    private static int NormalizeYear(int y, int calendar, string raw)
    {
        if (y >= 100 || calendar == GrampsCalendars.FrenchRepublican)
            return y;
        if (calendar != GrampsCalendars.Gregorian)
            throw McpToolErrors.ValidationError(
                $"Date \"{raw}\" has a two-digit year. Write the full year in a {GrampsCalendars.Names[calendar]} date.");
        return y >= 70 ? 1900 + y : 2000 + y;
    }
}
