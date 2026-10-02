using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Web;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsBatchFetchTests
{
    [Fact]
    public async Task GetByHandlesAsync_Loads_Distinct_Handles_In_One_List_Request()
    {
        var handler = new BatchHandler(FilteringServer);

        var events = await CreateClient(handler).GetByHandlesAsync<GrampsEvent>(
            "events", ["e1", " e2 ", "e1", "", null, "gone"], e => e.Handle, query: "profile=participants");

        Assert.Equal(["e1", "e2"], events.Keys.Order());
        Assert.Equal("E-e2", events["e2"].GrampsId);
        // The filter skips only handles that do not exist, so "gone" is not asked for again.
        Assert.Equal(["/api/events/?handles=e1,e2,gone&profile=participants&page=1&pagesize=3"], handler.Requests);
    }

    [Fact]
    public async Task GetByHandlesAsync_Splits_Large_Batches()
    {
        var handler = new BatchHandler(FilteringServer);
        var handles = Enumerable.Range(0, 120).Select(i => $"e{i:000}").ToArray();

        var events = await CreateClient(handler).GetByHandlesAsync<GrampsEvent>("events", handles, e => e.Handle);

        Assert.Equal(120, events.Count);
        Assert.Equal(
            ["pagesize=20", "pagesize=50", "pagesize=50"],
            handler.Requests.Select(r => r[r.LastIndexOf("pagesize=", StringComparison.Ordinal)..]).Order());
    }

    [Fact]
    public async Task GetByHandlesAsync_Uses_The_Object_Route_For_One_Handle()
    {
        var handler = new BatchHandler(FilteringServer);

        var events = await CreateClient(handler).GetByHandlesAsync<GrampsEvent>(
            "events", ["e1"], e => e.Handle, query: "profile=participants");

        Assert.Equal("E-e1", events["e1"].GrampsId);
        Assert.Equal(["/api/events/e1?profile=participants"], handler.Requests);
    }

    [Fact]
    public async Task GetByHandlesAsync_Falls_Back_When_The_Server_Rejects_The_Filter()
    {
        var handler = new BatchHandler(uri => uri.AbsolutePath == "/api/events/"
            ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent("""{"errors":{"query":{"handles":["Unknown field."]}}}""")
            }
            : FilteringServer(uri));
        var client = CreateClient(handler, "https://rejecting-gramps.test");

        var first = await client.GetByHandlesAsync<GrampsEvent>("events", ["e1", "e2", "gone"], e => e.Handle);
        var second = await client.GetByHandlesAsync<GrampsEvent>("events", ["e3", "e4"], e => e.Handle);

        Assert.Equal(["e1", "e2"], first.Keys.Order());
        Assert.Equal(["e3", "e4"], second.Keys.Order());
        // Only the first batch tries the filter; the server's answer is remembered.
        Assert.Equal(
            [
                "/api/events/?handles=e1,e2,gone&page=1&pagesize=3",
                "/api/events/e1", "/api/events/e2", "/api/events/e3", "/api/events/e4", "/api/events/gone",
            ],
            handler.Requests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetByHandlesAsync_Falls_Back_When_The_Server_Ignores_The_Filter()
    {
        // An older server sends an ordinary first page whatever handles were asked for.
        var handler = new BatchHandler(uri => uri.AbsolutePath == "/api/events/"
            ? Json("""[{ "handle": "e1", "gramps_id": "E-e1" }, { "handle": "other", "gramps_id": "E-other" }]""")
            : FilteringServer(uri));
        var client = CreateClient(handler, "https://ignoring-gramps.test");

        var first = await client.GetByHandlesAsync<GrampsEvent>("events", ["e1", "e2"], e => e.Handle);
        var second = await client.GetByHandlesAsync<GrampsEvent>("events", ["e3", "e4"], e => e.Handle);

        Assert.Equal(["e1", "e2"], first.Keys.Order());
        Assert.Equal(["e3", "e4"], second.Keys.Order());
        Assert.Equal(
            ["/api/events/?handles=e1,e2&page=1&pagesize=2", "/api/events/e2", "/api/events/e3", "/api/events/e4"],
            handler.Requests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetByHandlesAsync_Without_Handles_Sends_Nothing()
    {
        var handler = new BatchHandler(FilteringServer);

        var events = await CreateClient(handler).GetByHandlesAsync<GrampsEvent>("events", [" ", null], e => e.Handle);

        Assert.Empty(events);
        Assert.Empty(handler.Requests);
    }

    /// <summary>Gramps Web API 3.14+: the list route honors <c>handles</c>; every handle except "gone" exists.</summary>
    private static HttpResponseMessage FilteringServer(Uri uri)
    {
        if (uri.AbsolutePath == "/api/events/")
        {
            var handles = HttpUtility.ParseQueryString(uri.Query)["handles"]!.Split(',');
            return Json("[" + string.Join(",", handles.Where(h => h != "gone").Select(EventJson)) + "]");
        }

        var handle = uri.AbsolutePath["/api/events/".Length..];
        return handle == "gone" ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(EventJson(handle));
    }

    private static string EventJson(string handle) => $$"""{ "handle": "{{handle}}", "gramps_id": "E-{{handle}}" }""";

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    /// <summary>The "unsupported" verdict is kept per server and tree, so fallback tests use their own URL.</summary>
    private static GrampsApiClient CreateClient(HttpMessageHandler handler, string apiUrl = "https://gramps-web.test")
    {
        var config = new GrampsConfig(apiUrl, "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri(apiUrl) },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class BatchHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/token/")
                return Task.FromResult(Json("""{"access":"tok","refresh":"ref"}"""));

            _requests.Enqueue(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request.RequestUri));
        }
    }
}
