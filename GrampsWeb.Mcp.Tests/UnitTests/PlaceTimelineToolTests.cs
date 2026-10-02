using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PlaceTimelineToolTests
{
    [Fact]
    public async Task GetTimeline_Place_Reads_The_Place_Once_And_Its_Events_In_One_Batch()
    {
        var handler = new PlaceHandler(new Dictionary<string, string>
        {
            ["/api/places/place-h?backlinks=true"] = """
                { "handle": "place-h", "gramps_id": "P0001", "name": { "value": "Tver" },
                  "backlinks": { "event": ["e1", "e2", "e3", "gone"] } }
                """,
            // "gone" was deleted; e3 now points to another place.
            ["/api/events/?handles=e1,e2,e3,gone&profile=participants&page=1&pagesize=4"] = """
                [
                  { "handle": "e2", "gramps_id": "E0002", "type": "Death", "place": "place-h",
                    "date": { "dateval": [0, 0, 1950, false] },
                    "profile": { "participants": { "people": [
                      { "role": "Primary", "person": { "name_display": "Ivanov, Pyotr", "gramps_id": "I0002" } }
                    ] } } },
                  { "handle": "e1", "gramps_id": "E0001", "type": "Birth", "place": "place-h",
                    "date": { "dateval": [0, 0, 1900, false] },
                    "profile": { "participants": { "people": [
                      { "role": "Primary", "person": { "name_display": "Ivanov, Pyotr", "gramps_id": "I0002" } },
                      { "role": "Witness", "person": { "name_display": "Petrov, Ivan", "gramps_id": "I0003" } }
                    ] } } },
                  { "handle": "e3", "gramps_id": "E0003", "type": "Burial", "place": "elsewhere-h" }
                ]
                """,
        });

        string result;
        using (GrampsReadScope.Begin())
            result = await TimelineTools.GetTimeline("place", "place-h", client: CreateClient(handler));

        Assert.StartsWith("Place: Tver (P0001)\nTimeline (2 events):", result.Replace("\r\n", "\n"));
        Assert.Contains("Ivanov, Pyotr (I0002), Petrov, Ivan (I0003) [Witness]", result);
        Assert.DoesNotContain(" — Tver", result);
        Assert.DoesNotContain("Burial", result);
        Assert.True(
            result.IndexOf("Birth", StringComparison.Ordinal) < result.IndexOf("Death", StringComparison.Ordinal),
            result);
        Assert.Equal(
            [
                "/api/places/place-h?backlinks=true",
                "/api/events/?handles=e1,e2,e3,gone&profile=participants&page=1&pagesize=4",
            ],
            handler.Requests);
    }

    [Fact]
    public async Task GetTimeline_Place_Orders_Events_Of_One_Year_By_Sortval()
    {
        var handler = new PlaceHandler(new Dictionary<string, string>
        {
            ["/api/places/place-h?backlinks=true"] = """
                { "handle": "place-h", "gramps_id": "P0001", "name": { "value": "Orkney" },
                  "backlinks": { "event": ["e-dec", "e-jan"] } }
                """,
            ["/api/events/?handles=e-dec,e-jan&profile=participants&page=1&pagesize=2"] = """
                [
                  { "handle": "e-dec", "type": "Death", "place": "place-h",
                    "date": { "dateval": [5, 12, 960, false], "sortval": 2071999 } },
                  { "handle": "e-jan", "type": "Birth", "place": "place-h",
                    "date": { "dateval": [3, 1, 960, false], "sortval": 2071667 } }
                ]
                """,
        });

        string result;
        using (GrampsReadScope.Begin())
            result = await TimelineTools.GetTimeline("place", "place-h", client: CreateClient(handler));

        Assert.Contains("Timeline (2 events):", result);
        Assert.True(
            result.IndexOf("Birth", StringComparison.Ordinal) < result.IndexOf("Death", StringComparison.Ordinal),
            result);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class PlaceHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/token/")
                return Task.FromResult(Json("""{"access":"tok","refresh":"ref"}"""));

            var pathAndQuery = request.RequestUri!.PathAndQuery;
            _requests.Enqueue(pathAndQuery);
            return Task.FromResult(bodies.TryGetValue(pathAndQuery, out var body)
                ? Json(body)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
