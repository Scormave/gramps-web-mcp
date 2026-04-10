using System.Text.Json;
using GrampsWeb.Mcp.Dates;
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
}
