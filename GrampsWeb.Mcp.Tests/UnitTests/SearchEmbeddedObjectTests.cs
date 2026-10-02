using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class SearchEmbeddedObjectTests
{
    [Theory]
    [InlineData("person", "people", "\"primary_name\":{\"first_name\":\"Ada\"},\"event_ref_list\":[],\"birth_ref_index\":-1")]
    [InlineData("family", "families", "\"father_handle\":null,\"mother_handle\":null,\"type\":\"Married\"")]
    [InlineData("event", "events", "\"type\":\"Birth\",\"date\":null,\"place\":\"\"")]
    [InlineData("place", "places", "\"name\":{\"value\":\"London\"},\"place_type\":\"City\"")]
    [InlineData("source", "sources", "\"title\":\"Register\"")]
    [InlineData("citation", "citations", "\"source_handle\":null,\"page\":\"42\",\"confidence\":2")]
    [InlineData("note", "notes", "\"text\":{\"string\":\"Text\"},\"type\":\"General\"")]
    [InlineData("media", "media", "\"path\":\"photo.jpg\",\"mime\":\"image/jpeg\",\"desc\":\"Photo\"")]
    [InlineData("tag", "tags", "\"name\":\"Reviewed\"")]
    [InlineData("repository", "repositories", "\"name\":\"Archive\",\"type\":\"Library\"")]
    public async Task EmbeddedAndFetchedSummariesMatch_ForSingularAndPluralTypes(string type, string collection, string fields)
    {
        var body = "{\"handle\":\"h\"," + fields + "}";
        using var handler = new Handler(body);
        var client = Client(handler);
        var hit = new GrampsSearchHit { Handle = "h", ObjectType = type, GrampsId = "ID" };
        var expected = await SearchFormatter.FormatSearchResults([hit], client);
        Assert.Contains(handler.Paths, p => p.StartsWith($"/api/{collection}/h"));
        Assert.DoesNotContain("error loading", expected);

        foreach (var spelling in new[] { type, collection })
        {
            handler.Paths.Clear();
            hit.ObjectType = spelling;
            hit.Object = JsonSerializer.Deserialize<JsonElement>(body);
            Assert.Equal(expected, await SearchFormatter.FormatSearchResults([hit], client));
            Assert.DoesNotContain(handler.Paths, p => p.StartsWith($"/api/{collection}/h"));
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"string\"")]
    [InlineData("{}")]
    [InlineData("{\"handle\":\"h\"}")]
    [InlineData("{\"handle\":\"other\",\"title\":\"Wrong\"}")]
    [InlineData("{\"handle\":\"h\",\"title\":[]}")]
    public async Task UnusableEmbeddedObjectFallsBackToDetailRead(string embedded)
    {
        using var handler = new Handler("""{"handle":"h","title":"Correct"}""");
        var hit = new GrampsSearchHit
        {
            Handle = "h", ObjectType = "source", Object = JsonSerializer.Deserialize<JsonElement>(embedded)
        };
        var result = await SearchFormatter.FormatSearchResults([hit], Client(handler));
        Assert.Contains("Source: Correct", result);
        Assert.Equal(["/api/sources/h"], handler.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchToolPreservesOrderAndHandlesMixedEmbeddedAndLegacyHits(bool paged)
    {
        const string hits = """[{"handle":"a","object_type":"source","object":{"handle":"a","title":"First"}},{"handle":"h","object_type":"source"}]""";
        using var handler = new Handler("""{"handle":"h","title":"Second"}""")
        {
            SearchBody = paged ? "{\"objects\":" + hits + "}" : hits
        };
        var result = await SearchTools.Search("query", client: Client(handler));
        Assert.True(result.IndexOf("Source: First", StringComparison.Ordinal) < result.IndexOf("Source: Second", StringComparison.Ordinal));
        Assert.Contains("Source: First", result);
        Assert.Contains("Source: Second", result);
        Assert.Equal(["/api/search/?query=query&page=1&pagesize=20", "/api/sources/h"], handler.Paths);
    }

    [Fact]
    public async Task PersonMissingEventReferencesFallsBackInsteadOfLosingBirth()
    {
        using var handler = new Handler("""{"handle":"h","primary_name":{"first_name":"Ada"},"event_ref_list":[{"ref":"birth"}],"birth_ref_index":0}""");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "person",
            Object = JsonSerializer.Deserialize<JsonElement>("""{"handle":"h","primary_name":{"first_name":"Ada"}}""")
        }], Client(handler));
        Assert.Contains("b. ", result);
        Assert.Equal(["/api/people/h", "/api/events/birth", "/api/places/place"], handler.Paths);
    }

    [Fact]
    public async Task EmbeddedEventLoadsOnlyMissingPlace()
    {
        using var handler = new Handler("{}");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "event",
            Object = JsonSerializer.Deserialize<JsonElement>("""{"handle":"h","type":"Birth","date":null,"place":"place"}""")
        }], Client(handler));
        Assert.Contains("London", result);
        Assert.Contains("/api/places/place", handler.Paths);
        Assert.DoesNotContain(handler.Paths, p => p.StartsWith("/api/events/"));
    }

    [Theory]
    [InlineData("""{"handle":"h","type":"Birth","date":null,"place":""}""", "Event: Birth")]
    [InlineData("""{"handle":"h","type":"","date":null,"place":""}""", "Event")]
    [InlineData("""{"handle":"h","type":"Death","date":{"dateval":[0,0,1950,false]},"place":""}""", "Event: Death — 1950")]
    public async Task EventLineLeavesOutMissingParts(string evt, string expectedLine)
    {
        using var handler = new Handler("{}");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "event", Object = JsonSerializer.Deserialize<JsonElement>(evt)
        }], Client(handler));
        Assert.Contains($"\n{expectedLine} — handle: h", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task FamilyWithoutEmbeddedParentsKeepsSingleExtendedFetch()
    {
        const string family = """{"handle":"h","father_handle":"father","mother_handle":"mother","type":"Married"}""";
        using var handler = new Handler("""{"handle":"h","father_handle":"father","mother_handle":"mother","type":"Married","extended":{"father":{"primary_name":{"first_name":"John"}},"mother":{"primary_name":{"first_name":"Jane"}}}}""");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "family", Object = JsonSerializer.Deserialize<JsonElement>(family)
        }], Client(handler));
        Assert.Contains("John and Jane", result);
        Assert.Contains("/api/families/h?extend=father_handle,mother_handle", handler.Paths);
        Assert.DoesNotContain(handler.Paths, p => p.StartsWith("/api/people/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CitationUsesEmbeddedSourceOrFetchesOnlySource(bool embeddedSource)
    {
        var body = "{\"handle\":\"h\",\"source_handle\":\"source\",\"page\":\"42\",\"confidence\":2"
            + (embeddedSource ? ",\"extended\":{\"source\":{\"title\":\"Register\"}}" : "") + "}";
        using var handler = new Handler("""{"title":"Register"}""");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "citation", Object = JsonSerializer.Deserialize<JsonElement>(body)
        }], Client(handler));
        Assert.Contains("Register — p. 42", result);
        if (embeddedSource) Assert.Empty(handler.Paths);
        else Assert.Equal(["/api/sources/source"], handler.Paths);
    }

    private static GrampsApiClient Client(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    private sealed class Handler(string detail) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string SearchBody { get; init; } = "[]";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var token = path.StartsWith("/api/token/");
            if (!token) Paths.Add(path);
            var body = token ? """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""
                : path.StartsWith("/api/search/") ? SearchBody
                : path.StartsWith("/api/types/") ? "[]"
                : path == "/api/events/birth" ? """{"type":"Birth","place":"place"}"""
                : path == "/api/places/place" ? """{"name":{"value":"London"}}"""
                : detail;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
