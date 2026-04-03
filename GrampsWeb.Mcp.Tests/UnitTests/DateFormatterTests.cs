using Xunit;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Unit tests for GrampsValueFormatter date formatting methods.
/// Tests all 7 date modifiers and edge cases (BCE, partial dates, text-only).
/// </summary>
public class DateFormatterTests
{
    [Theory]
    [InlineData(0, "12 Nov 1899")]  // modifier=0 (None)
    [InlineData(1, "before 12 Nov 1899")]  // modifier=1 (Before)
    [InlineData(2, "after 12 Nov 1899")]   // modifier=2 (After)
    [InlineData(3, "about 12 Nov 1899")]   // modifier=3 (About)
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

        Assert.Contains("1850 B.C.E.", result);
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
            Year = 1735,
            Slash = true,
            Text = null,
            NewYear = 0
        };

        var result = GrampsValueFormatter.FormatDate(date);

        Assert.Contains("1735/1736", result);
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

        Assert.Contains("between", result);
        Assert.Contains("1800", result);
        Assert.Contains("1850", result);
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

        Assert.Contains("from", result);
        Assert.Contains("1800", result);
        Assert.Contains("1850", result);
    }
}
