using GrampsWeb.Mcp.Tools;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class TimelineQueryStringTests
{
    [Fact]
    public void BuildTimelineQueryString_UsesEventClassesCommaDelimited()
    {
        var qs = PersonTools.BuildTimelineQueryString(
            ["vital", "family"], null, null, null, false);
        Assert.Equal("?event_classes=vital%2Cfamily&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_RelativesAndRelativeEventClasses_AreCommaDelimited()
    {
        var qs = PersonTools.BuildTimelineQueryString(
            null, ["father", "mother"], ["vital"], null, false);
        Assert.Equal("?relatives=father%2Cmother&relative_event_classes=vital&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_NormalizesPaddedDatesForApiRegex()
    {
        var qs = PersonTools.BuildTimelineQueryString(
            null, null, null, "1999/01/01-2010/01/01", false);
        Assert.Equal("?dates=1999%2F1%2F1-2010%2F1%2F1&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_Default_SendsDiscardEmptyFalse()
    {
        var qs = PersonTools.BuildTimelineQueryString(null, null, null, null, false);
        Assert.Equal("?discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_StrictUndated_OmitsDiscardEmptyParam()
    {
        var qs = PersonTools.BuildTimelineQueryString(
            null, null, null, null, false, includeUndated: false);
        Assert.Equal("", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_StrictUndated_WithEventClasses_NoDiscardEmptyFalse()
    {
        var qs = PersonTools.BuildTimelineQueryString(
            ["vital"], null, null, null, false, includeUndated: false);
        Assert.Equal("?event_classes=vital", qs);
    }

    [Fact]
    public void NormalizeTimelineDatesForGrampsApi_OpenEndedEnd_StripsZeros()
    {
        var n = PersonTools.NormalizeTimelineDatesForGrampsApi("2000/03/05-");
        Assert.Equal("2000/3/5-", n);
    }

    [Fact]
    public void NormalizeTimelineDatesForGrampsApi_OpenEndedStart_StripsZeros()
    {
        var n = PersonTools.NormalizeTimelineDatesForGrampsApi("-2000/03/05");
        Assert.Equal("-2000/3/5", n);
    }
}
