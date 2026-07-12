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
