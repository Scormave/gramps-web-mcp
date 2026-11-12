using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsReadScopeTests
{
    [Fact]
    public async Task ConcurrentReadsShareWireResponseButNotMutableObjects()
    {
        using var handler = new Handler();
        var client = Client(handler);
        using (GrampsReadScope.Begin())
        {
            var people = await Task.WhenAll(Enumerable.Range(0, 10)
                .Select(_ => client.GetAsync<GrampsPerson>("/api/people/p1")));
            Assert.Equal(1, handler.Count("/api/people/p1"));
            people[0].Handle = "modified";
            Assert.Equal("p1", people[1].Handle);
            var json = await client.GetAsync<JsonElement>("/api/people/p1");
            Assert.Equal("p1", json.GetProperty("handle").GetString());
            Assert.Equal(1, handler.Count("/api/people/p1"));
            await client.GetAsync<JsonElement>("/api/people/p1?extend=all");
            Assert.Equal(1, handler.Count("/api/people/p1?extend=all"));
        }
        using (GrampsReadScope.Begin())
            await client.GetAsync<JsonElement>("/api/people/p1");
        Assert.Equal(2, handler.Count("/api/people/p1"));
        await client.GetAsync<JsonElement>("/api/people/p1");
        await client.GetAsync<JsonElement>("/api/people/p1");
        Assert.Equal(4, handler.Count("/api/people/p1"));
    }

    [Fact]
    public async Task ClientsAndConcurrentScopesAreIsolated()
    {
        using var handler = new Handler();
        var first = Client(handler);
        var second = Client(handler);
        using (GrampsReadScope.Begin())
        {
            await first.GetAsync<JsonElement>("/api/people/p1");
            await second.GetAsync<JsonElement>("/api/people/p1");
        }
        Assert.Equal(2, handler.Count("/api/people/p1"));
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var scope = GrampsReadScope.Begin();
            await first.GetAsync<JsonElement>("/api/people/p1");
            await first.GetAsync<JsonElement>("/api/people/p1");
        }));
        Assert.Equal(4, handler.Count("/api/people/p1"));
    }

    [Fact]
    public async Task FailedReadCanBeRetriedWithinScope()
    {
        using var handler = new Handler { FailNextRead = true };
        var client = Client(handler);
        using var scope = GrampsReadScope.Begin();
        await Assert.ThrowsAsync<GrampsWeb.Mcp.Exceptions.GrampsApiException>(
            () => client.GetAsync<JsonElement>("/api/people/p1"));
        await client.GetAsync<JsonElement>("/api/people/p1");
        Assert.Equal(2, handler.Count("/api/people/p1"));
    }

    [Fact]
    public async Task SearchLoadsOnlyRequiredCategories()
    {
        using var handler = new Handler();
        var client = Client(handler);
        using var scope = GrampsReadScope.Begin();
        var tables = await GrampsDefaultTypeLabels.PrefetchForSearchAsync(
            ["event", "events", "place", "person", "citation"], client);
        Assert.Equal("Birth", tables.EventTypes![12]);
        Assert.Equal("City", tables.PlaceTypes![4]);
        Assert.Null(tables.NameTypes);
        Assert.Equal(2, handler.ReadCount);
    }

    [Fact]
    public async Task NameVocabulariesAreIndexedByValueWithoutCustomTypes()
    {
        using var handler = new Handler();
        var client = Client(handler);
        using var scope = GrampsReadScope.Begin();
        var results = await Task.WhenAll(
            GrampsDefaultTypeLabels.LoadNameTypeLabelsAsync(client),
            GrampsDefaultTypeLabels.LoadNameOriginTypeLabelsAsync(client));
        Assert.Equal(["Custom", "Also Known As", "Birth Name"], results[0]);
        Assert.Equal(["Custom", "", "Inherited"], results[1]);
        Assert.Equal(0, handler.Count("/api/types/custom/"));
    }

    private static GrampsApiClient Client(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    private sealed class Handler : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Paths { get; } = new();
        public bool FailNextRead { get; set; }
        public int ReadCount => Paths.Values.Sum();
        public int Count(string path) => Paths.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/"))
                return Response("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");
            Paths.AddOrUpdate(path, 1, (_, n) => n + 1);
            await Task.Yield();
            if (FailNextRead)
            {
                FailNextRead = false;
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("retry") };
            }
            if (path.StartsWith("/api/people/"))
            {
                var id = request.RequestUri.Segments.Last();
                return Response(JsonSerializer.Serialize(new { handle = id }));
            }
            return Response(path switch
            {
                "/api/types/default/event_types/map" => """{"-1":"Unknown","0":"Custom","12":"Birth"}""",
                "/api/types/default/place_types/map" => """{"-1":"Unknown","0":"Custom","4":"City"}""",
                "/api/types/default/name_types/map" => """{"-1":"Unknown","0":"Custom","1":"Also Known As","2":"Birth Name"}""",
                "/api/types/default/name_origin_types/map" => """{"-1":"Unknown","0":"Custom","1":"","2":"Inherited"}""",
                "/api/types/custom/" => """{"name_types":["Custom Name"],"name_origin_types":["Custom Origin"]}""",
                _ => "[]"
            });
        }

        private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
