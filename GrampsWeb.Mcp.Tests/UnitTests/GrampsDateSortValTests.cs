using System.Text.Json;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using GrampsWeb.Mcp.Serialization;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsDateSortValTests
{
    [Fact]
    public void TryCompute_MatchesGrampsPython_gregorian_sdn_2000_01_10()
    {
        var d = new DateRequest
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Day = 10,
            Month = 1,
            Year = 2000,
            Slash = false
        };
        Assert.Equal(2451554, GrampsDateSortVal.TryComputeForDateRequest(d));
    }

    [Fact]
    public void Serialize_DateRequest_IncludesSortvalForGregorianExact()
    {
        var d = new DateRequest
        {
            Calendar = 0,
            Modifier = 0,
            Quality = 0,
            Day = 10,
            Month = 1,
            Year = 2000,
            Slash = false
        };
        var json = JsonSerializer.Serialize(d, GrampsJson.Options);
        Assert.Contains("\"sortval\":2451554", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCompute_TextOnly_ReturnsNull()
    {
        var d = new DateRequest { Calendar = 0, Modifier = 6, Text = "foo" };
        Assert.Null(GrampsDateSortVal.TryComputeForDateRequest(d));
    }

    // Expected values from Gramps Date.set(...).sortval.
    [Theory]
    [InlineData(1, 0, false, 20, 7, 1856, 2399163)]
    [InlineData(0, 0, false, 1, 8, 1856, 2399163)]
    [InlineData(2, 0, false, 15, 1, 5600, 2393006)]
    [InlineData(3, 0, false, 15, 3, 12, 2379932)]
    [InlineData(4, 0, false, 15, 1, 1300, 2422784)]
    [InlineData(5, 0, false, 15, 1, 1250, 2391058)]
    [InlineData(6, 0, false, 30, 2, 1712, 2346425)]
    [InlineData(1, 0, false, 0, 0, 1850, 2396771)]
    [InlineData(1, 2, false, 10, 3, 1735, 2354835)]
    [InlineData(1, 2, false, 30, 3, 1735, 2354490)]
    [InlineData(0, 2, false, 1, 4, 1735, 2354481)]
    [InlineData(1, 3, false, 5, 9, 1700, 2341865)]
    [InlineData(6, 1, false, 1, 3, 1700, 2341677)]
    [InlineData(0, 0, true, 10, 2, 1735, 2354807)]
    public void TryCompute_Matches_Gramps_For_Every_Calendar_And_New_Year(
        int calendar, int newYear, bool slash, int day, int month, int year, int expected)
    {
        var d = new DateRequest { Calendar = calendar, NewYear = newYear, Slash = slash, Day = day, Month = month, Year = year };

        Assert.Equal(expected, GrampsDateSortVal.TryComputeForDateRequest(d));
    }

    [Fact]
    public void Serialize_DateRequest_IncludesSortvalForJulian()
    {
        var d = new DateRequest { Calendar = 1, Day = 20, Month = 7, Year = 1856 };

        var json = JsonSerializer.Serialize(d, GrampsJson.Options);

        Assert.Contains("\"calendar\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"sortval\":2399163", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelineSortKey_Is_Computed_For_A_Stored_Julian_Date_Without_Sortval()
    {
        var d = new GrampsDate { Calendar = 1, Day = 20, Month = 7, Year = 1856 };

        Assert.Equal(2399163, GrampsDateSortVal.TryGetTimelineSortKey(d));
    }

    [Theory]
    [InlineData(0, 0, 0, 29, 2, 1900, 0, 0, 0, false)]
    [InlineData(1, 0, 0, 29, 2, 1900, 0, 0, 0, true)]
    [InlineData(2, 0, 0, 1, 7, 5783, 0, 0, 0, false)]
    [InlineData(2, 0, 0, 1, 7, 5784, 0, 0, 0, true)]
    [InlineData(2, 0, 0, 0, 7, 5600, 0, 0, 0, true)]
    [InlineData(3, 0, 0, 6, 13, 12, 0, 0, 0, false)]
    [InlineData(3, 0, 0, 6, 13, 11, 0, 0, 0, true)]
    [InlineData(6, 0, 0, 29, 2, 1700, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, false)]
    [InlineData(1, 0, 4, 0, 0, 1850, 0, 0, 1860, true)]
    [InlineData(1, 0, 4, 1, 1, 1850, 30, 2, 1860, false)]
    [InlineData(1, 2, 4, 1, 1, 1735, 1, 4, 1736, true)]
    [InlineData(1, 2, 4, 1, 4, 1735, 1, 1, 1736, false)]
    [InlineData(1, 2, 4, 1, 4, 1735, 1, 1, 1737, false)]
    public void PassesGrampsDateCheck_Matches_Gramps_Date_Set(
        int calendar, int newYear, int modifier, int day, int month, int year,
        int endDay, int endMonth, int endYear, bool expected)
    {
        var d = new DateRequest
        {
            Calendar = calendar, NewYear = newYear, Modifier = modifier, Day = day, Month = month, Year = year,
            EndDay = endDay, EndMonth = endMonth, EndYear = endYear
        };

        Assert.Equal(expected, GrampsDateSortVal.PassesGrampsDateCheck(d));
    }
}
