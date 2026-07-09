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
        var req = GrampsRequestMapping.ToPrimaryPlaceNameRequest("Boise City", null, existing);

        Assert.Equal("Boise City", req.Value);
        Assert.Equal("en", req.Lang);
        Assert.Equal(1900, req.Date!.Year);
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
