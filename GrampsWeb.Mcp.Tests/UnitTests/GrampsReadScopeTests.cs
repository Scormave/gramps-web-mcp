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
    public async Task TwentyPeopleShareOnePlace_IdenticalOutputWithNineteenFewerRequests()
    {
        using var handler = new Handler();
        var client = Client(handler);
        var hits = Enumerable.Range(1, 20).Select(i => new GrampsSearchHit
        {
            ObjectType = "person", Handle = $"p{i}", GrampsId = $"I{i:0000}"
        }).ToArray();
        var baseline = await SearchFormatter.FormatSearchResults(hits, client);
        Assert.Equal(60, handler.ReadCount);
        handler.Paths.Clear();
        string cached;
        using (GrampsReadScope.Begin())
            cached = await SearchFormatter.FormatSearchResults(hits, client);
        Assert.Equal(baseline, cached);
        Assert.Equal(41, handler.ReadCount);
        Assert.Equal(1, handler.Count("/api/places/shared"));
        Assert.DoesNotContain(handler.Paths.Keys, p => p.StartsWith("/api/types/"));
    }

    [Fact]
    public async Task SearchLoadsOnlyRequiredCategories_AndSharesBulkFallback()
    {
        using var handler = new Handler();
        var client = Client(handler);
        using var scope = GrampsReadScope.Begin();
        var tables = await GrampsDefaultTypeLabels.PrefetchForSearchAsync(
            ["event", "events", "place", "person", "citation"], client);
        Assert.Equal("Birth", Assert.Single(tables.EventTypes!));
        Assert.Equal("City", Assert.Single(tables.PlaceTypes!));
        Assert.Null(tables.NameTypes);
        Assert.Equal(3, handler.ReadCount);
        Assert.Equal(1, handler.Count("/api/types/default/"));
    }

    [Fact]
    public async Task CustomVocabularyLoadedOnceAndKeepsIndexOrder()
    {
        using var handler = new Handler();
        var client = Client(handler);
        using var scope = GrampsReadScope.Begin();
        var results = await Task.WhenAll(
            GrampsDefaultTypeLabels.LoadNameTypeLabelsAsync(client),
            GrampsDefaultTypeLabels.LoadNameOriginTypeLabelsAsync(client));
        Assert.Equal(["Birth Name", "Custom Name"], results[0]);
        Assert.Equal(["Inherited", "Custom Origin"], results[1]);
        Assert.Equal(1, handler.Count("/api/types/custom/"));
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
                return Response(JsonSerializer.Serialize(new
                {
                    handle = id, primary_name = new { first_name = id },
                    birth_ref_index = 0, event_ref_list = new[] { new { @ref = "e" + id } }
                }));
            }
            if (path.StartsWith("/api/events/"))
                return Response("""{"type":"Birth","place":"shared"}""");
            return Response(path switch
            {
                "/api/places/shared" => """{"name":{"value":"Shared city"}}""",
                "/api/types/default/" => """{"event_types":["Birth"],"place_types":["City"]}""",
                "/api/types/default/name_types" => """["Birth Name"]""",
                "/api/types/default/name_origin_types" => """["Inherited"]""",
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
