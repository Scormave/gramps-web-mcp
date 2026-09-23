using GrampsWeb.Mcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class TimelineQueryStringTests
{
    [Fact]
    public void BuildTimelineQueryString_UsesEventClassesCommaDelimited()
    {
        var qs = TimelineTools.BuildQueryString(
            ["vital", "family"], null, null, null);
        Assert.Equal("?event_classes=vital%2Cfamily&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_RelativesAndRelativeEventClasses_AreCommaDelimited()
    {
        var qs = TimelineTools.BuildQueryString(
            null, ["father", "mother"], ["vital"], null);
        Assert.Equal("?relatives=father%2Cmother&relative_event_classes=vital&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_NormalizesPaddedDatesForApiRegex()
    {
        var qs = TimelineTools.BuildQueryString(
            null, null, null, "1999/01/01-2010/01/01");
        Assert.Equal("?dates=1999%2F1%2F1-2010%2F1%2F1&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_Default_SendsDiscardEmptyFalse()
    {
        var qs = TimelineTools.BuildQueryString(null, null, null, null);
        Assert.Equal("?discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_StrictUndated_OmitsDiscardEmptyParam()
    {
        var qs = TimelineTools.BuildQueryString(
            null, null, null, null, includeUndated: false);
        Assert.Equal("", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_StrictUndated_WithEventClasses_NoDiscardEmptyFalse()
    {
        var qs = TimelineTools.BuildQueryString(
            ["vital"], null, null, null, includeUndated: false);
        Assert.Equal("?event_classes=vital", qs);
    }

    [Fact]
    public void NormalizeTimelineDatesForGrampsApi_OpenEndedEnd_StripsZeros()
    {
        var n = TimelineTools.NormalizeDatesForGrampsApi("2000/03/05-");
        Assert.Equal("2000/3/5-", n);
    }

    [Fact]
    public void NormalizeTimelineDatesForGrampsApi_OpenEndedStart_StripsZeros()
    {
        var n = TimelineTools.NormalizeDatesForGrampsApi("-2000/03/05");
        Assert.Equal("-2000/3/5", n);
    }

    [Fact]
    public async Task GetTimeline_RejectsUnsupportedObjectType()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => TimelineTools.GetTimeline("event", "E0001"));

        Assert.Contains("person, family, or place", error.Message);
    }

    [Fact]
    public async Task GetTimeline_RejectsRelativeFiltersForFamilyAndPlace()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => TimelineTools.GetTimeline("family", "F0001", relatives: ["father"]));

        Assert.Contains("only for objectType person", error.Message);
    }
}
