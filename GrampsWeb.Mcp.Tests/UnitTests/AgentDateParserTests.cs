using System.Text.Json;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class AgentDateParserTests
{
    [Fact]
    public void ToDateRequestOrNull_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(AgentDateParser.ToDateRequestOrNull(null));
        Assert.Null(AgentDateParser.ToDateRequestOrNull("   "));
    }

    [Fact]
    public void Iso_YearMonthDay_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1990-03-15");
        Assert.NotNull(d);
        Assert.Equal(0, d!.Modifier);
        Assert.Equal(15, d.Day);
        Assert.Equal(3, d.Month);
        Assert.Equal(1990, d.Year);
    }

    [Fact]
    public void Iso_YearMonth_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1990-03");
        Assert.NotNull(d);
        Assert.Equal(3, d!.Month);
        Assert.Equal(1990, d.Year);
        Assert.Equal(0, d.Day);
    }

    [Fact]
    public void Iso_YearOnly_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1920");
        Assert.NotNull(d);
        Assert.Equal(1920, d!.Year);
    }

    [Fact]
    public void PrefixBefore_AppliesModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("before 1920");
        Assert.NotNull(d);
        Assert.Equal(1, d!.Modifier);
        Assert.Equal(1920, d.Year);
    }

    [Fact]
    public void BetweenYears_RangeModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("between 1800 and 1850");
        Assert.NotNull(d);
        Assert.Equal(4, d!.Modifier);
        Assert.Equal(1800, d.Year);
        Assert.Equal(1850, d.EndYear);
    }

    [Fact]
    public void FromTo_SpanModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("from 1800 to 1850");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1800, d.Year);
        Assert.Equal(1850, d.EndYear);
    }

    [Fact]
    public void DayMonthYear_Order_ParsesSlashes()
    {
        var d = AgentDateParser.ToDateRequestOrNull("15/03/1990", DateComponentOrder.DayMonthYear);
        Assert.NotNull(d);
        Assert.Equal(15, d!.Day);
        Assert.Equal(3, d.Month);
        Assert.Equal(1990, d.Year);
    }

    [Fact]
    public void MonthDayYear_Order_ParsesSlashes()
    {
        var d = AgentDateParser.ToDateRequestOrNull("03/15/1990", DateComponentOrder.MonthDayYear);
        Assert.NotNull(d);
        Assert.Equal(15, d!.Day);
        Assert.Equal(3, d.Month);
        Assert.Equal(1990, d.Year);
    }

    [Fact]
    public void Iso_WithSlashTriplet_ThrowsMcpException()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("15/03/1990", DateComponentOrder.Iso));
        Assert.Contains("dateComponentOrder", ex.Message);
    }

    [Fact]
    public void UnrecognizedString_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("early spring 1847"));
        Assert.Contains("Unrecognized date", ex.Message);
        Assert.Contains("get_reference", ex.Message);
    }

    [Fact]
    public void English_DayAbbrMonthYear_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1 Jul 1919");
        Assert.NotNull(d);
        Assert.Equal(0, d!.Modifier);
        Assert.Equal(1, d.Day);
        Assert.Equal(7, d.Month);
        Assert.Equal(1919, d.Year);
    }

    [Fact]
    public void English_DayFullMonthYear_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("5 July 1944");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Day);
        Assert.Equal(7, d.Month);
        Assert.Equal(1944, d.Year);
    }

    [Fact]
    public void English_DayFullMonthYear_OptionalComma_Parses()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1 July, 1919");
        Assert.NotNull(d);
        Assert.Equal(1, d!.Day);
        Assert.Equal(7, d.Month);
        Assert.Equal(1919, d.Year);
    }

    [Fact]
    public void English_MonthYear_AbbrAndFull_Parse()
    {
        var abbr = AgentDateParser.ToDateRequestOrNull("Jul 1919");
        Assert.Equal(0, abbr!.Day);
        Assert.Equal(7, abbr.Month);
        Assert.Equal(1919, abbr.Year);

        var full = AgentDateParser.ToDateRequestOrNull("October 1929");
        Assert.Equal(0, full!.Day);
        Assert.Equal(10, full.Month);
        Assert.Equal(1929, full.Year);
    }

    [Fact]
    public void English_FromTo_SpanModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("from 1 Oct 1929 to 27 Sep 1937");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1, d.Day);
        Assert.Equal(10, d.Month);
        Assert.Equal(1929, d.Year);
        Assert.Equal(27, d.EndDay);
        Assert.Equal(9, d.EndMonth);
        Assert.Equal(1937, d.EndYear);
    }

    [Fact]
    public void English_FromOnly_IsFromModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("from 5 Jul 1944");
        Assert.NotNull(d);
        Assert.Equal(7, d!.Modifier);
        Assert.Equal(5, d.Day);
        Assert.Equal(7, d.Month);
        Assert.Equal(1944, d.Year);
    }

    [Fact]
    public void English_DashBetween_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1 Oct 1929-5 Jul 1944");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1, d.Day);
        Assert.Equal(10, d.Month);
        Assert.Equal(1929, d.Year);
        Assert.Equal(5, d.EndDay);
        Assert.Equal(7, d.EndMonth);
        Assert.Equal(1944, d.EndYear);
    }

    [Fact]
    public void English_BeforePrefix_AppliesModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("before 1 Apr 1920");
        Assert.NotNull(d);
        Assert.Equal(1, d!.Modifier);
        Assert.Equal(1, d.Day);
        Assert.Equal(4, d.Month);
        Assert.Equal(1920, d.Year);
    }

    [Fact]
    public void English_OpenEndedDash_Default_IsFromTo()
    {
        var after = AgentDateParser.ToDateRequestOrNull("5 Jul 1944-");
        Assert.Equal(7, after!.Modifier);
        Assert.Equal(5, after.Day);
        Assert.Equal(7, after.Month);
        Assert.Equal(1944, after.Year);

        var before = AgentDateParser.ToDateRequestOrNull("-1 Apr 1920");
        Assert.Equal(8, before!.Modifier);
        Assert.Equal(1, before.Day);
        Assert.Equal(4, before.Month);
        Assert.Equal(1920, before.Year);
    }

    [Theory]
    [InlineData("1856-07-20 (Julian)", 1, 0, 20, 7, 1856)]
    [InlineData("20 Jul 1856 (julian)", 1, 0, 20, 7, 1856)]
    [InlineData("1900-02-29 (Julian)", 1, 0, 29, 2, 1900)]
    [InlineData("1856-07-20 (Gregorian)", 0, 0, 20, 7, 1856)]
    [InlineData("5784-07-01 (Hebrew)", 2, 0, 1, 7, 5784)]
    [InlineData("5783-13-29 (Hebrew)", 2, 0, 29, 13, 5783)]
    [InlineData("12-03-15 (French Republican)", 3, 0, 15, 3, 12)]
    [InlineData("11-13-06 (french republican)", 3, 0, 6, 13, 11)]
    [InlineData("1300-01-15 (Persian)", 4, 0, 15, 1, 1300)]
    [InlineData("1250-01-15 (Islamic)", 5, 0, 15, 1, 1250)]
    [InlineData("1712-02-30 (Swedish)", 6, 0, 30, 2, 1712)]
    [InlineData("1735-03-10 (Julian, Mar25)", 1, 2, 10, 3, 1735)]
    [InlineData("1735-03-10 (Mar25)", 0, 2, 10, 3, 1735)]
    [InlineData("1700-09-05 ( Julian , Sep1 )", 1, 3, 5, 9, 1700)]
    [InlineData("1700-03-01 (Swedish, Mar1)", 6, 1, 1, 3, 1700)]
    public void CalendarSuffix_Sets_Calendar_And_NewYear_And_Keeps_The_Numbers(
        string input, int calendar, int newYear, int day, int month, int year)
    {
        var d = AgentDateParser.ToDateRequestOrNull(input);

        Assert.NotNull(d);
        Assert.Equal((calendar, newYear, 0, day, month, year), (d!.Calendar, d.NewYear, d.Modifier, d.Day, d.Month, d.Year));
    }

    [Fact]
    public void InputGuide_Calendar_Examples_Parse()
    {
        var guide = JsonSerializer.SerializeToElement(GrampsResources.BuildDateInputGuidePayload());

        foreach (var example in guide.GetProperty("calendars").GetProperty("examples").EnumerateArray())
            Assert.NotNull(AgentDateParser.ToDateRequestOrNull(example.GetString()));
    }

    [Fact]
    public void CalendarSuffix_Applies_To_Both_Ends_Of_A_Range()
    {
        var d = AgentDateParser.ToDateRequestOrNull("between 1850 and 1860 (julian)");

        Assert.NotNull(d);
        Assert.Equal((4, 1, 1850, 1860), (d!.Modifier, d.Calendar, d.Year, d.EndYear));
    }

    [Fact]
    public void CalendarSuffix_Follows_Quality_And_Modifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("estimated about 1856-07 (Julian)");

        Assert.NotNull(d);
        Assert.Equal((1, 3, 1, 7, 1856), (d!.Quality, d.Modifier, d.Calendar, d.Month, d.Year));
    }

    [Theory]
    [InlineData("12 (French Republican)", 0, 0, 12, 0)]
    [InlineData("about 12-03 (French Republican)", 0, 3, 12, 0)]
    [InlineData("from 11 to 12 (French Republican)", 0, 0, 11, 12)]
    public void FrenchRepublican_Short_Years_Are_Years_Of_The_Republic(
        string input, int day, int month, int year, int endYear)
    {
        var d = AgentDateParser.ToDateRequestOrNull(input);

        Assert.NotNull(d);
        Assert.Equal((3, day, month, year, endYear), (d!.Calendar, d.Day, d.Month, d.Year, d.EndYear));
    }

    [Fact]
    public void FrenchRepublican_DayMonthYear_Keeps_A_Short_Year()
    {
        var d = AgentDateParser.ToDateRequestOrNull("15/03/12 (French Republican)", DateComponentOrder.DayMonthYear);

        Assert.NotNull(d);
        Assert.Equal((3, 15, 3, 12), (d!.Calendar, d.Day, d.Month, d.Year));
    }

    [Fact]
    public void Gregorian_Date_That_Exists_Only_In_Julian_Suggests_The_Calendar()
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull("1900-02-29"));

        Assert.Contains("doesn't exist in the Gregorian calendar", ex.Message);
        Assert.Contains("add the calendar: 1900-02-29 (Julian)", ex.Message);
    }

    [Theory]
    [InlineData("1856-02-30", "Gregorian")]
    [InlineData("between 1850-01-01 and 1851-02-29", "Gregorian")]
    [InlineData("1856-02-30 (Julian)", "Julian")]
    [InlineData("1700-02-29 (Swedish)", "Swedish")]
    [InlineData("12-13-06 (French Republican)", "French Republican")]
    [InlineData("1299-12-30 (Persian)", "Persian")]
    [InlineData("1250-12-30 (Islamic)", "Islamic")]
    public void Date_Missing_From_Its_Calendar_ThrowsValidationError(string input, string calendarName)
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull(input));

        Assert.Contains($"doesn't exist in the {calendarName} calendar.", ex.Message);
        Assert.DoesNotContain("add the calendar", ex.Message);
    }

    [Fact]
    public void Hebrew_AdarII_In_A_Common_Year_Explains_The_Months()
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull("5783-07-01 (Hebrew)"));

        Assert.Contains("doesn't exist in the Hebrew calendar", ex.Message);
        Assert.Contains("Adar II (month 7) exists only in leap years", ex.Message);
    }

    [Theory]
    [InlineData("5783-14-01 (Hebrew)", "use 1–13")]
    [InlineData("12-14-01 (French Republican)", "use 1–13")]
    [InlineData("1856-13-01 (Julian)", "use 1–12")]
    [InlineData("1300-13 (Persian)", "use 1–12")]
    public void Month_Beyond_The_Calendar_ThrowsValidationError(string input, string expected)
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull(input));

        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("15 Jan 5600 (Hebrew)", "\"Jan\"", "(1–13): 5600-01-15 (Hebrew)")]
    [InlineData("March 1300 (Persian)", "\"March\"", "(1–12): 1300-01-15 (Persian)")]
    public void English_Month_Name_In_A_Calendar_With_Its_Own_Months_ThrowsValidationError(
        string input, string month, string example)
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull(input));

        Assert.Contains($"names the month {month}", ex.Message);
        Assert.Contains(example, ex.Message);
    }

    [Theory]
    [InlineData("1856-07-20 (Old Style)")]
    [InlineData("1856-07-20 (Julian, Mar26)")]
    [InlineData("1856-07-20 (Mar25, Julian)")]
    public void Unknown_Calendar_Suffix_ThrowsValidationError(string input)
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull(input));

        Assert.Contains("isn't a Gramps calendar or new year", ex.Message);
    }

    [Fact]
    public void NewYear_On_A_Calendar_With_Its_Own_New_Year_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() => AgentDateParser.ToDateRequestOrNull("5600-01-15 (Hebrew, Mar25)"));

        Assert.Contains("the Hebrew calendar has its own new year", ex.Message);
    }

    [Fact]
    public void Calendar_Inside_The_Date_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("between 1850 (Julian) and 1860 (Julian)"));

        Assert.Contains("has parentheses inside the date", ex.Message);
    }

    [Fact]
    public void Range_With_A_New_Year_Is_Accepted_When_It_Starts_Before_The_New_Year_Day()
    {
        var d = AgentDateParser.ToDateRequestOrNull("between 1735-01-01 and 1736-04-01 (Julian, Mar25)");

        Assert.NotNull(d);
        Assert.Equal((4, 1, 2, 1735, 1736), (d!.Modifier, d.Calendar, d.NewYear, d.Year, d.EndYear));
    }

    [Fact]
    public void Range_With_A_New_Year_Starting_On_Or_After_The_New_Year_Day_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("between 1735-04-01 and 1736-01-01 (Julian, Mar25)"));

        Assert.Contains("Gramps can't store a range or span with the new year Mar25", ex.Message);
    }

    [Fact]
    public void Two_Digit_Year_Outside_The_Gregorian_Calendar_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("20/07/56 (Julian)", DateComponentOrder.DayMonthYear));

        Assert.Contains("has a two-digit year. Write the full year in a Julian date.", ex.Message);
    }

    [Fact]
    public void English_UsMonthDayYear_ThrowsValidationError()
    {
        var ex = Assert.Throws<McpException>(() =>
            AgentDateParser.ToDateRequestOrNull("July 1, 1919"));
        Assert.Contains("Unrecognized date", ex.Message);
    }

    [Fact]
    public void YearDashYear_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1708-1927");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1708, d.Year);
        Assert.Equal(1927, d.EndYear);
    }

    [Fact]
    public void YearDashYear_RangePreference_IsRange()
    {
        var d = AgentDateParser.ToDateRequestOrNull(
            "1708-1927", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.NotNull(d);
        Assert.Equal(4, d!.Modifier);
        Assert.Equal(1708, d.Year);
        Assert.Equal(1927, d.EndYear);
    }

    [Fact]
    public void IsoFullDashRange_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1914-08-31-1924-01-26");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(31, d.Day);
        Assert.Equal(8, d.Month);
        Assert.Equal(1914, d.Year);
        Assert.Equal(26, d.EndDay);
        Assert.Equal(1, d.EndMonth);
        Assert.Equal(1924, d.EndYear);
    }

    [Fact]
    public void IsoFullDashRange_RangePreference_IsRange()
    {
        var d = AgentDateParser.ToDateRequestOrNull(
            "1914-08-31-1924-01-26", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.NotNull(d);
        Assert.Equal(4, d!.Modifier);
    }

    [Fact]
    public void IsoMonthDashRange_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1914-08-1924-01");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(8, d.Month);
        Assert.Equal(1914, d.Year);
        Assert.Equal(1, d.EndMonth);
        Assert.Equal(1924, d.EndYear);
    }

    [Fact]
    public void MixedPrecision_YearToFull_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1703-1914-08-31");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1703, d.Year);
        Assert.Equal(0, d.Month);
        Assert.Equal(0, d.Day);
        Assert.Equal(1914, d.EndYear);
        Assert.Equal(8, d.EndMonth);
        Assert.Equal(31, d.EndDay);
    }

    [Fact]
    public void MixedPrecision_FullToYear_Default_IsSpan()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1914-08-31-1924");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1914, d.Year);
        Assert.Equal(8, d.Month);
        Assert.Equal(31, d.Day);
        Assert.Equal(1924, d.EndYear);
        Assert.Equal(0, d.EndMonth);
        Assert.Equal(0, d.EndDay);
    }

    [Fact]
    public void MixedPrecision_RangePreference_IsRange()
    {
        var d = AgentDateParser.ToDateRequestOrNull(
            "1703-1914-08-31", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.NotNull(d);
        Assert.Equal(4, d!.Modifier);
    }

    [Fact]
    public void OpenEnded_YearAfter_Default_IsFrom()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1991-");
        Assert.NotNull(d);
        Assert.Equal(7, d!.Modifier);
        Assert.Equal(1991, d.Year);
    }

    [Fact]
    public void OpenEnded_YearBefore_Default_IsTo()
    {
        var d = AgentDateParser.ToDateRequestOrNull("-1722");
        Assert.NotNull(d);
        Assert.Equal(8, d!.Modifier);
        Assert.Equal(1722, d.Year);
    }

    [Fact]
    public void OpenEnded_IsoAfter_Default_IsFrom()
    {
        var d = AgentDateParser.ToDateRequestOrNull("1924-01-26-");
        Assert.NotNull(d);
        Assert.Equal(7, d!.Modifier);
        Assert.Equal(26, d.Day);
        Assert.Equal(1, d.Month);
        Assert.Equal(1924, d.Year);
    }

    [Fact]
    public void OpenEnded_RangePreference_IsAfterBefore()
    {
        var after = AgentDateParser.ToDateRequestOrNull(
            "1991-", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.Equal(2, after!.Modifier);

        var before = AgentDateParser.ToDateRequestOrNull(
            "-1722", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.Equal(1, before!.Modifier);
    }

    [Fact]
    public void Explicit_FromDate_IsFromModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("from 1991");
        Assert.NotNull(d);
        Assert.Equal(7, d!.Modifier);
        Assert.Equal(1991, d.Year);
    }

    [Fact]
    public void Explicit_ToDate_IsToModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("to 1917");
        Assert.NotNull(d);
        Assert.Equal(8, d!.Modifier);
        Assert.Equal(1917, d.Year);
    }

    [Fact]
    public void Explicit_FromDate_IgnoresRangePreference()
    {
        var d = AgentDateParser.ToDateRequestOrNull(
            "from 1991-09-06", DateComponentOrder.Iso, DateIntervalPreference.Range);
        Assert.NotNull(d);
        Assert.Equal(7, d!.Modifier);
        Assert.Equal(6, d.Day);
        Assert.Equal(9, d.Month);
        Assert.Equal(1991, d.Year);
    }

    [Fact]
    public void Between_IsoDates_RangeModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("between 1914-08-31 and 1924-01-26");
        Assert.NotNull(d);
        Assert.Equal(4, d!.Modifier);
        Assert.Equal(1914, d.Year);
        Assert.Equal(8, d.Month);
        Assert.Equal(31, d.Day);
        Assert.Equal(1924, d.EndYear);
        Assert.Equal(1, d.EndMonth);
        Assert.Equal(26, d.EndDay);
    }

    [Fact]
    public void FromTo_IsoDates_SpanModifier()
    {
        var d = AgentDateParser.ToDateRequestOrNull("from 1914-08-31 to 1924-01-26");
        Assert.NotNull(d);
        Assert.Equal(5, d!.Modifier);
        Assert.Equal(1914, d.Year);
        Assert.Equal(1924, d.EndYear);
    }
}
