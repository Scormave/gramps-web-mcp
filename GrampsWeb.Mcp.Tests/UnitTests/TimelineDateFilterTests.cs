using GrampsWeb.Mcp.Dates;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class TimelineDateFilterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_WithoutDates_HasNoFilter(string? dates) =>
        Assert.Null(TimelineDateFilter.Parse(dates));

    [Theory]
    [InlineData("1850", "1850/1/1-1850/12/31")]
    [InlineData("1850-03", "1850/3/1-1850/3/31")]
    [InlineData("1900-02", "1900/2/1-1900/2/28")]
    [InlineData("1904-02", "1904/2/1-1904/2/29")]
    [InlineData("1850-03-05", "1850/3/5-1850/3/5")]
    [InlineData("5 Mar 1850", "1850/3/5-1850/3/5")]
    [InlineData("1850-1860", "1850/1/1-1860/12/31")]
    [InlineData("1850-03 - 1851-06", "1850/3/1-1851/6/30")]
    [InlineData("between 1850-03 and 1851", "1850/3/1-1851/12/31")]
    [InlineData("from 1850 to 1860-06", "1850/1/1-1860/6/30")]
    [InlineData("from 1850", "1850/1/1-")]
    [InlineData("1850-", "1850/1/1-")]
    [InlineData("to 1900", "-1900/12/31")]
    [InlineData("-1900", "-1900/12/31")]
    [InlineData("before 1900", "-1899/12/31")]
    [InlineData("before 1900-05-01", "-1900/4/30")]
    [InlineData("after 1850", "1851/1/1-")]
    [InlineData("after 1850-02", "1850/3/1-")]
    public void Parse_CoversEveryDayOfTheDates(string dates, string apiDates) =>
        Assert.Equal(apiDates, TimelineDateFilter.Parse(dates)!.ApiDates);

    [Theory]
    [InlineData("1856-07-20 (Julian)", "1856/8/1-1856/8/1")]
    [InlineData("1850 (Julian)", "1850/1/13-1851/1/12")]
    [InlineData("from 1918-01-31 (Julian)", "1918/2/13-")]
    [InlineData("1735-03-10 (Julian, Mar25)", "1735/3/21-1735/3/21")]
    [InlineData("1735-04-10 (Julian, Mar25)", "1734/4/21-1734/4/21")]
    [InlineData("5600-01 (Hebrew)", "1839/9/9-1839/10/8")]
    public void Parse_ConvertsOtherCalendarsToGregorian(string dates, string apiDates) =>
        Assert.Equal(apiDates, TimelineDateFilter.Parse(dates)!.ApiDates);

    [Theory]
    [InlineData("1999/1/1-2010/12/31", "1999/1/1-2010/12/31")]
    [InlineData("1999/01/01-2010/01/01", "1999/1/1-2010/1/1")]
    [InlineData("2000/03/05-", "2000/3/5-")]
    [InlineData("-2000/03/05", "-2000/3/5")]
    [InlineData("1850/1/1", "1850/1/1-1850/1/1")]
    [InlineData("800/1/1-", "800/1/1-")]
    public void Parse_KeepsTheGrampsWebForm(string dates, string apiDates) =>
        Assert.Equal(apiDates, TimelineDateFilter.Parse(dates)!.ApiDates);

    [Fact]
    public void Parse_GivesTheSameBoundsAsSerialDays()
    {
        var range = TimelineDateFilter.Parse("1850-1860")!.Range;

        Assert.Equal(GrampsCalendars.ToSdn(GrampsCalendars.Gregorian, 1850, 1, 1), range.MinInclusive);
        Assert.Equal(GrampsCalendars.ToSdn(GrampsCalendars.Gregorian, 1860, 12, 31), range.MaxInclusive);
        Assert.Equal(new TimelineSdnRange(null, range.MinInclusive - 1), TimelineDateFilter.Parse("before 1850")!.Range);
    }

    [Theory]
    [InlineData("about 1850", "approximate")]
    [InlineData("abt 1850", "approximate")]
    [InlineData("estimated 1850-1860", "approximate")]
    [InlineData("calc between 1850 and 1860", "approximate")]
    [InlineData("1850 (Mar25)", "new year")]
    [InlineData("1850-03 (Julian, Mar25)", "new year")]
    [InlineData("01/02/1850", "Put the year first")]
    [InlineData("1.2.1850", "Put the year first")]
    [InlineData("between 1860 and 1850", "ends before it starts")]
    [InlineData("sometime", "Unrecognized date")]
    public void Parse_RejectsDatesItCantBound(string dates, string message)
    {
        var error = Assert.Throws<McpException>(() => TimelineDateFilter.Parse(dates));

        Assert.Contains(message, error.Message);
    }
}
