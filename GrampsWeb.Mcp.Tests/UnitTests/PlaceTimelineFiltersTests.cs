using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PlaceTimelineFiltersTests
{
    [Fact]
    public void MatchesEventClasses_NoFilter_AcceptsAny()
    {
        Assert.True(PlaceTimelineFilters.MatchesEventClasses("Birth", null));
        Assert.True(PlaceTimelineFilters.MatchesEventClasses("Birth", []));
    }

    [Fact]
    public void MatchesEventClasses_Vital_MatchesBirth()
    {
        Assert.True(PlaceTimelineFilters.MatchesEventClasses("Birth", ["vital"]));
        Assert.False(PlaceTimelineFilters.MatchesEventClasses("Marriage", ["vital"]));
    }

    [Fact]
    public void MatchesEventClasses_Custom_MatchesNonDefaultType()
    {
        Assert.True(PlaceTimelineFilters.MatchesEventClasses("My Custom Thing", ["custom"]));
        Assert.False(PlaceTimelineFilters.MatchesEventClasses("Birth", ["custom"]));
    }

    [Fact]
    public void TryParseDateRange_Closed_BothBounds()
    {
        var r = PlaceTimelineFilters.TryParseDateRange("1999/1/1-2010/12/31");
        Assert.NotNull(r);
        Assert.NotNull(r.Value.MinInclusive);
        Assert.NotNull(r.Value.MaxInclusive);
        Assert.True(r.Value.MinInclusive <= r.Value.MaxInclusive);
    }

    [Fact]
    public void Passes_DateRange_ExcludesOutside()
    {
        var range = PlaceTimelineFilters.TryParseDateRange("2000/1/1-2000/12/31");
        Assert.NotNull(range);
        var opts = new PlaceTimelineCollectOptions(null, true);
        var inRange = new GrampsEvent
        {
            Type = "Birth",
            Date = GrampsDate.ExactDate(15, 6, 2000)
        };
        var outRange = new GrampsEvent
        {
            Type = "Birth",
            Date = GrampsDate.ExactDate(1, 1, 1990)
        };
        Assert.True(PlaceTimelineFilters.Passes(inRange, opts, range));
        Assert.False(PlaceTimelineFilters.Passes(outRange, opts, range));
    }

    [Fact]
    public void Passes_IncludeUndatedFalse_DropsZeroSortVal()
    {
        var opts = new PlaceTimelineCollectOptions(null, false);
        var undated = new GrampsEvent { Type = "Birth", Date = new GrampsDate { SortVal = 0 } };
        Assert.False(PlaceTimelineFilters.Passes(undated, opts, null));
    }
}
