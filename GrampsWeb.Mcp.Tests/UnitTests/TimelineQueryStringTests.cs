using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void BuildTimelineQueryString_Default_SendsDiscardEmptyFalse()
    {
        var qs = TimelineTools.BuildQueryString(null, null, null, null);
        Assert.Equal("?discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_PersonWithoutDates_KeepsEventsOutsideThePersonsFirstAndLastEvent()
    {
        var qs = TimelineTools.BuildQueryString(null, ["wife"], null, null, personTimeline: true);
        Assert.Equal("?relatives=wife&first=false&last=false&discard_empty=false", qs);
    }

    [Fact]
    public void BuildTimelineQueryString_PersonWithDates_SendsOnlyTheRange()
    {
        var qs = TimelineTools.BuildQueryString(null, null, null, "1850/1/1-1900/12/31", personTimeline: true);
        Assert.Equal("?dates=1850%2F1%2F1-1900%2F12%2F31&discard_empty=false", qs);
    }

    [Theory]
    [InlineData("person", null, "/api/people/anchor-h/timeline?first=false&last=false&discard_empty=false")]
    [InlineData("person", "1850/01/01-1900/12/31", "/api/people/anchor-h/timeline?dates=1850%2F1%2F1-1900%2F12%2F31&discard_empty=false")]
    [InlineData("person", "1850-1900", "/api/people/anchor-h/timeline?dates=1850%2F1%2F1-1900%2F12%2F31&discard_empty=false")]
    [InlineData("family", "before 1900 (Julian)", "/api/families/anchor-h/timeline?dates=-1900%2F1%2F12&discard_empty=false")]
    [InlineData("family", null, "/api/families/anchor-h/timeline?discard_empty=false")]
    public async Task GetTimeline_Cuts_A_Person_Timeline_Only_To_The_Given_Dates(string objectType, string? dates, string request)
    {
        // Gramps Web cuts a person timeline to the person's first and last event, matching an "about 1876"
        // birth as 1826 to 1926, which dropped relatives' events; family timelines are not cut.
        var handler = new RecordingHandler();

        await TimelineTools.GetTimeline(objectType, "anchor-h", dates: dates, client: CreateClient(handler));

        Assert.Equal([request], handler.Requests);
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
    public async Task GetTimeline_RejectsDatesItCantBound_BeforeAnyRequest()
    {
        var handler = new RecordingHandler();

        var error = await Assert.ThrowsAsync<McpException>(
            () => TimelineTools.GetTimeline("person", "anchor-h", dates: "about 1850", client: CreateClient(handler)));

        Assert.Contains("approximate", error.Message);
        Assert.Empty(handler.Requests);
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

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    /// <summary>Records the timeline requests and answers each with an empty timeline.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            if (path.StartsWith("/api/types/", StringComparison.Ordinal))
                return Task.FromResult(Json("[]"));

            _requests.Enqueue(path);
            return Task.FromResult(path.Contains("/timeline", StringComparison.Ordinal)
                ? Json("[]")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
