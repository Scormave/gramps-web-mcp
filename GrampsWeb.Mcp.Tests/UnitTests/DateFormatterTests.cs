using Xunit;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Unit tests for GrampsValueFormatter date formatting methods.
/// Tests all 7 date modifiers and edge cases (BCE, partial dates, text-only). Dates read as Gramps Web
/// shows them in its default ISO format, so cards agree with the server-formatted dates in timelines.
/// </summary>
public class DateFormatterTests
{
    [Theory]
    [InlineData(0, "1899-11-12")]  // modifier=0 (None)
    [InlineData(1, "before 1899-11-12")]  // modifier=1 (Before)
    [InlineData(2, "after 1899-11-12")]   // modifier=2 (After)
    [InlineData(3, "about 1899-11-12")]   // modifier=3 (About)
    public void FormatDate_WithModifiers_ReturnsCorrectString(int modifier, string expected)
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = modifier,
            Quality = 0,
            Day = 12,
            Month = 11,
            Year = 1899,
            Slash = false,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatDate_TextOnly_ReturnsTextField()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 6,
            Quality = 0,
            Day = 0,
            Month = 0,
            Year = 0,
            Slash = false,
            Text = "Christmas 1847",
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal("Christmas 1847", result);
    }

    [Fact]
    public void FormatDate_BCE_ReturnsBCENotation()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Year = -1850,
            Slash = false,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal("1850 B.C.E.", result);
    }

    [Fact]
    public void FormatDate_PartialDate_OmitsDayMonth()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Year = 1899,
            Slash = false,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal("1899", result);
    }

    [Fact]
    public void FormatDate_DoubleDate_IncludesSlash()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Day = 1,
            Month = 1,
            Year = 1736,
            Slash = true,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        // Gramps stores the later year of a dual-dated year.
        Assert.Equal("1735/6-01-01", result);
    }

    [Fact]
    public void FormatDate_NullDate_ReturnsUnknown()
    {
        var result = GrampsValueFormatter.FormatDate(null!);

        Assert.Equal("Unknown date", result);
    }

    [Fact]
    public void FormatDate_Range_ReturnsBetweenString()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 4,
            Quality = 0,
            Year = 1800,
            EndYear = 1850,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal("between 1800 and 1850", result);
    }

    [Fact]
    public void FormatDate_Span_ReturnsFromToString()
    {
        var date = new GrampsDate
        {
            Calendar = 0,
            Modifier = 5,
            Quality = 0,
            Year = 1800,
            EndYear = 1850,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Equal("from 1800 to 1850", result);
    }

    [Theory]
    [InlineData(12, 11, 1899, "1899-11-12")]
    [InlineData(0, 11, 1899, "1899-11")]
    [InlineData(0, 0, 1899, "1899")]
    [InlineData(12, 0, 1899, "1899-00-12")]
    [InlineData(5, 3, 988, "988-03-05")]
    [InlineData(1, 3, -1850, "1850-03-01 B.C.E.")]
    public void FormatDate_Writes_Iso_Like_Gramps_Web(int day, int month, int year, string expected)
    {
        var date = new GrampsDate { Day = day, Month = month, Year = year };

        Assert.Equal(expected, GrampsValueFormatter.FormatDate(date));
    }

    [Theory]
    [InlineData(1736, "1735/6")]
    [InlineData(1740, "1739/40")]
    [InlineData(1700, "1699/700")]
    public void FormatDate_Shortens_The_Second_Year_Of_A_Dual_Date_Like_Gramps(int year, string expected)
    {
        var date = new GrampsDate { Year = year, Slash = true };

        Assert.Equal(expected, GrampsValueFormatter.FormatDate(date));
    }

    [Theory]
    [InlineData(1, 3, 0, 0, "estimated about 1930-08")]
    [InlineData(2, 0, 0, 0, "calculated 1930-08")]
    [InlineData(0, 0, 1, 0, "1930-08 (Julian)")]
    [InlineData(0, 1, 1, 2, "before 1930-08 (Julian, Mar25)")]
    [InlineData(0, 0, 0, 3, "1930-08 (Sep1)")]
    public void FormatDate_Shows_Quality_And_A_Non_Standard_Calendar(
        int quality, int modifier, int calendar, int newYear, string expected)
    {
        var date = new GrampsDate
        {
            Quality = quality, Modifier = modifier, Calendar = calendar, NewYear = newYear, Month = 8, Year = 1930
        };

        Assert.Equal(expected, GrampsValueFormatter.FormatDate(date));
    }

    [Fact]
    public void FormatDate_Qualifies_A_Whole_Range()
    {
        var date = new GrampsDate { Quality = 1, Modifier = 4, Calendar = 1, Year = 1850, EndYear = 1860 };

        Assert.Equal("estimated between 1850 and 1860 (Julian)", GrampsValueFormatter.FormatDate(date));
    }

    [Theory]
    [InlineData(0, 0, 21, 8, 1930, 0, 0, 0)]
    [InlineData(0, 0, 0, 8, 1930, 0, 0, 0)]
    [InlineData(0, 0, 0, 0, 1930, 0, 0, 0)]
    [InlineData(0, 0, 5, 3, 988, 0, 0, 0)]
    [InlineData(0, 1, 0, 0, 1930, 0, 0, 0)]
    [InlineData(0, 2, 0, 8, 1930, 0, 0, 0)]
    [InlineData(0, 3, 21, 8, 1930, 0, 0, 0)]
    [InlineData(0, 4, 1, 1, 1850, 0, 0, 1860)]
    [InlineData(0, 5, 1, 10, 1929, 27, 9, 1937)]
    [InlineData(0, 7, 0, 10, 1929, 0, 0, 0)]
    [InlineData(0, 8, 0, 0, 1937, 0, 0, 0)]
    [InlineData(1, 3, 0, 8, 1930, 0, 0, 0)]
    [InlineData(2, 0, 21, 8, 1930, 0, 0, 0)]
    [InlineData(1, 4, 0, 0, 1850, 0, 0, 1860)]
    public void FormatDate_Output_Parses_Back_To_The_Same_Date(
        int quality, int modifier, int day, int month, int year, int endDay, int endMonth, int endYear)
    {
        var date = new GrampsDate
        {
            Quality = quality, Modifier = modifier, Day = day, Month = month, Year = year,
            EndDay = endDay, EndMonth = endMonth, EndYear = endYear
        };
        var text = GrampsValueFormatter.FormatDate(date);

        var parsed = AgentDateParser.ToDateRequestOrNull(text);

        Assert.NotNull(parsed);
        Assert.Equal(
            (date.Modifier, date.Quality, date.Calendar, date.Day, date.Month, date.Year, date.EndDay, date.EndMonth, date.EndYear),
            (parsed!.Modifier, parsed.Quality, parsed.Calendar, parsed.Day, parsed.Month, parsed.Year, parsed.EndDay, parsed.EndMonth, parsed.EndYear));
    }

    [Theory]
    [InlineData(1, 0, 0, 20, 7, 1856, 0)]
    [InlineData(1, 2, 1, 10, 3, 1735, 0)]
    [InlineData(0, 3, 0, 0, 8, 1930, 0)]
    [InlineData(2, 0, 0, 0, 7, 5784, 0)]
    [InlineData(3, 0, 0, 15, 3, 12, 0)]
    [InlineData(3, 0, 5, 0, 0, 11, 12)]
    [InlineData(4, 0, 3, 0, 0, 1300, 0)]
    [InlineData(5, 0, 0, 15, 1, 1250, 0)]
    [InlineData(6, 0, 0, 30, 2, 1712, 0)]
    public void FormatDate_Output_With_A_Calendar_Parses_Back_To_The_Same_Date(
        int calendar, int newYear, int modifier, int day, int month, int year, int endYear)
    {
        var date = new GrampsDate
        {
            Calendar = calendar, NewYear = newYear, Modifier = modifier, Day = day, Month = month, Year = year,
            EndYear = endYear
        };
        var text = GrampsValueFormatter.FormatDate(date);

        var parsed = AgentDateParser.ToDateRequestOrNull(text);

        Assert.NotNull(parsed);
        Assert.Equal(
            (date.Calendar, date.NewYear, date.Modifier, date.Day, date.Month, date.Year, date.EndYear),
            (parsed!.Calendar, parsed.NewYear, parsed.Modifier, parsed.Day, parsed.Month, parsed.Year, parsed.EndYear));
    }
}
