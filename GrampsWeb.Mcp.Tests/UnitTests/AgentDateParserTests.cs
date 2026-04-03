using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;
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
    public void UnrecognizedString_TextOnlyFallback()
    {
        var d = AgentDateParser.ToDateRequestOrNull("early spring 1847");
        Assert.NotNull(d);
        Assert.Equal(6, d!.Modifier);
        Assert.Equal("early spring 1847", d.Text);
    }
}
