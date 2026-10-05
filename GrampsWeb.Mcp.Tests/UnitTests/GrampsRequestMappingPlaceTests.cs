using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsRequestMappingPlaceTests
{
    [Fact]
    public void ToPrimaryPlaceNameRequest_Preserves_Lang_When_Only_Name_Changes()
    {
        var existing = new GrampsPlaceName { Value = "Boise", Lang = "en", Date = GrampsDate.YearOnly(1900) };
        var req = GrampsRequestMapping.ToPrimaryPlaceNameRequest("Boise City", null, null, existing);

        Assert.Equal("Boise City", req.Value);
        Assert.Equal("en", req.Lang);
        Assert.Equal(1900, req.Date!.Year);
    }

    [Fact]
    public void ToPrimaryPlaceNameRequest_Parses_Date_As_Place_Span()
    {
        var existing = new GrampsPlaceName { Value = "Leningrad", Lang = "ru" };
        var req = GrampsRequestMapping.ToPrimaryPlaceNameRequest(null, null, "1924-01-26-1991-09-06", existing);

        Assert.Equal("Leningrad", req.Value);
        Assert.Equal("ru", req.Lang);
        Assert.Equal(5, req.Date!.Modifier);
        Assert.Equal(1924, req.Date.Year);
        Assert.Equal(1991, req.Date.EndYear);
    }

    [Fact]
    public void ToPrimaryPlaceNameRequest_Empty_Date_And_Lang_Remove_Them()
    {
        var existing = new GrampsPlaceName { Value = "Boise", Lang = "en", Date = GrampsDate.YearOnly(1900) };
        var req = GrampsRequestMapping.ToPrimaryPlaceNameRequest(null, "", " ", existing);

        Assert.Equal("Boise", req.Value);
        Assert.Null(req.Lang);
        Assert.Null(req.Date);
    }

    [Fact]
    public void ToPlaceRefRequests_Maps_Dates()
    {
        var refs = new[]
        {
            new GrampsPlaceRef { Ref = "h1", Date = GrampsDate.YearOnly(1920) }
        };

        var mapped = GrampsRequestMapping.ToPlaceRefRequests(refs);
        Assert.NotNull(mapped);
        Assert.Single(mapped!);
        Assert.Equal("h1", mapped[0].Ref);
        Assert.Equal(1920, mapped[0].Date!.Year);
    }
}
