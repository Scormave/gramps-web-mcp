using GrampsWeb.Mcp.Dates;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsCalendarsTests
{
    // Expected values from gramps.gen.lib.gcalendar.
    [Theory]
    [InlineData(0, 1582, 10, 15, 2299161)]
    [InlineData(1, 1582, 10, 5, 2299161)]
    [InlineData(1, 1918, 2, 1, 2421639)]
    [InlineData(2, 5785, 1, 1, 2460587)]
    [InlineData(2, 5784, 13, 29, 2460586)]
    [InlineData(2, 5784, 7, 1, 2460381)]
    [InlineData(3, 1, 1, 1, 2375840)]
    [InlineData(3, 14, 1, 1, 2380588)]
    [InlineData(4, 1, 1, 1, 1948321)]
    [InlineData(5, 1, 1, 1, 1948440)]
    [InlineData(6, 1700, 3, 1, 2342042)]
    [InlineData(6, 1712, 2, 30, 2346425)]
    [InlineData(6, 1753, 2, 17, 2361389)]
    [InlineData(6, 1753, 3, 1, 2361390)]
    public void ToSdn_And_FromSdn_Match_Gramps(int calendar, int year, int month, int day, int sdn)
    {
        Assert.Equal(sdn, GrampsCalendars.ToSdn(calendar, year, month, day));
        Assert.Equal((year, month, day), GrampsCalendars.FromSdn(calendar, sdn));
    }

    [Fact]
    public void Names_Are_Listed_By_Gramps_Calendar_Code()
    {
        Assert.Equal(
            ["Gregorian", "Julian", "Hebrew", "French Republican", "Persian", "Islamic", "Swedish"],
            GrampsCalendars.Names);
    }
}
