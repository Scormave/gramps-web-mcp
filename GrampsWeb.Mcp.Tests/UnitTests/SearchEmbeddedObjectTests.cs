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
    [InlineData("person", "people", "\"profile\":{\"name_display\":\"Lovelace, Ada\",\"birth\":{\"date\":\"1815\"}}")]
    [InlineData("family", "families", "\"type\":\"Married\",\"profile\":{\"father\":{\"name_display\":\"Smith, John\"},\"mother\":{}}")]
    [InlineData("event", "events", "\"type\":\"Birth\",\"date\":null,\"profile\":{\"place_name\":\"London\"}")]
    [InlineData("place", "places", "\"name\":{\"value\":\"London\"},\"place_type\":\"City\"")]
    [InlineData("source", "sources", "\"title\":\"Register\"")]
    [InlineData("citation", "citations", "\"page\":\"42\",\"confidence\":2,\"profile\":{\"source\":{\"title\":\"Register\"}}")]
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
        Assert.Equal(["/api/search/?query=query&page=1&pagesize=20&profile=self", "/api/sources/h"], handler.Paths);
    }

    [Fact]
    public async Task PersonWithoutProfileIsReadAgainWithProfile()
    {
        using var handler = new Handler("""
            {"handle":"h","primary_name":{"first_name":"Ada"},
             "profile":{"name_display":"Lovelace, Ada","birth":{"type":"Birth","date":"1815","place_name":"London"},"death":{}}}
            """);
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "person", GrampsId = "I0001",
            Object = JsonSerializer.Deserialize<JsonElement>("""{"handle":"h","primary_name":{"first_name":"Ada"}}""")
        }], Client(handler));
        Assert.Contains("\nPerson: Lovelace, Ada, b. 1815 in London — handle: h | gramps_id: I0001\n", result.Replace("\r\n", "\n"));
        Assert.Equal(["/api/people/h?profile=self"], handler.Paths);
    }

    [Fact]
    public async Task GrampsIdComesFromTheObjectWhenTheHitHasNone()
    {
        using var handler = new Handler("""{"handle":"b","gramps_id":"S0002","title":"Second"}""");
        var result = (await SearchFormatter.FormatSearchResults(
        [
            new GrampsSearchHit
            {
                Handle = "a", ObjectType = "source",
                Object = JsonSerializer.Deserialize<JsonElement>("""{"handle":"a","gramps_id":"S0001","title":"First"}""")
            },
            new GrampsSearchHit { Handle = "b", ObjectType = "source" },
        ], Client(handler))).Replace("\r\n", "\n");

        // The embedded object and the object read afterwards both supply the ID.
        Assert.Contains("\nSource: First — handle: a | gramps_id: S0001\n", result);
        Assert.Contains("\nSource: Second — handle: b | gramps_id: S0002\n", result);
    }

    [Fact]
    public async Task HitsWithoutObjectsAreReadInOneBatchPerType()
    {
        using var handler = new Handler("{}")
        {
            Bodies =
            {
                ["/api/people/?handles=p1,p2,gone&profile=self&page=1&pagesize=3"] = """
                    [
                      {"handle":"p2","profile":{"name_display":"Petrov, Ivan","birth":{},"death":{"type":"Death","date":"1950"}}},
                      {"handle":"p1","profile":{"name_display":"Ivanov, Pyotr","birth":{"date":"1900","place_name":"Tver"},"death":{}}}
                    ]
                    """,
                ["/api/events/?handles=e1,e2&profile=self&page=1&pagesize=2"] = """
                    [
                      {"handle":"e1","type":"Birth","date":{"dateval":[0,0,1900,false]},"profile":{"place_name":"Tver"}},
                      {"handle":"e2","type":"Death","date":null,"profile":{}}
                    ]
                    """,
            }
        };
        GrampsSearchHit Hit(string type, string handle) => new() { ObjectType = type, Handle = handle };
        GrampsSearchHit[] hits =
        [
            Hit("person", "p1"), Hit("event", "e1"), Hit("person", "p2"), Hit("event", "e2"), Hit("person", "gone"),
            new() { ObjectType = "source", Handle = "s1", Object = JsonSerializer.Deserialize<JsonElement>("""{"handle":"s1","title":"Register"}""") },
        ];

        var result = (await SearchFormatter.FormatSearchResults(hits, Client(handler))).Replace("\r\n", "\n");

        // Rows keep the order of the hits, not of the batch replies; "gone" was deleted after indexing.
        Assert.Contains(
            "Person: Ivanov, Pyotr, b. 1900 in Tver — handle: p1\n" +
            "Event: Birth — 1900 — Tver — handle: e1\n" +
            "Person: Petrov, Ivan, d. 1950 — handle: p2\n" +
            "Event: Death — handle: e2\n" +
            "person:  — handle: gone (error loading details)\n" +
            "Source: Register — handle: s1\n",
            result);
        Assert.Equal(
            [
                "/api/events/?handles=e1,e2&profile=self&page=1&pagesize=2",
                "/api/people/?handles=p1,p2,gone&profile=self&page=1&pagesize=3",
            ],
            handler.Paths.Where(p => !p.StartsWith("/api/types/")).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("""{"handle":"h","type":"Birth","date":null,"profile":{}}""", "Event: Birth")]
    [InlineData("""{"handle":"h","type":"","date":null,"profile":{"place_name":""}}""", "Event")]
    [InlineData("""{"handle":"h","type":"Death","date":{"dateval":[0,0,1950,false]},"profile":{}}""", "Event: Death — 1950")]
    [InlineData("""{"handle":"h","type":"Death","date":null,"profile":{"place_name":"London"}}""", "Event: Death — London")]
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
    public async Task FamilyPartnersComeFromTheProfile()
    {
        using var handler = new Handler("{}");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "family",
            Object = JsonSerializer.Deserialize<JsonElement>("""
                {"handle":"h","father_handle":"father","mother_handle":"mother","type":"Married",
                 "profile":{"father":{"name_display":"Smith, John"},"mother":{"name_display":"Doe, Jane"}}}
                """)
        }], Client(handler));
        Assert.Contains("Family: Smith, John and Doe, Jane (Married)", result);
        Assert.DoesNotContain(handler.Paths, p => !p.StartsWith("/api/types/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CitationSourceComesFromTheProfile(bool embeddedProfile)
    {
        var body = "{\"handle\":\"h\",\"source_handle\":\"source\",\"page\":\"42\",\"confidence\":2"
            + (embeddedProfile ? ",\"profile\":{\"source\":{\"title\":\"Register\"}}" : "") + "}";
        using var handler = new Handler("""{"handle":"h","page":"42","confidence":2,"profile":{"source":{"title":"Register"}}}""");
        var result = await SearchFormatter.FormatSearchResults([new GrampsSearchHit
        {
            Handle = "h", ObjectType = "citation", Object = JsonSerializer.Deserialize<JsonElement>(body)
        }], Client(handler));
        Assert.Contains("Citation: Register — p. 42 (confidence: Normal)", result);
        if (embeddedProfile) Assert.Empty(handler.Paths);
        else Assert.Equal(["/api/citations/h?profile=self"], handler.Paths);
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
        /// <summary>Bodies by path and query, served before the defaults below.</summary>
        public Dictionary<string, string> Bodies { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            var token = path.StartsWith("/api/token/");
            if (!token) lock (Paths) Paths.Add(path);
            var body = token ? """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""
                : Bodies.TryGetValue(path, out var known) ? known
                : path.StartsWith("/api/search/") ? SearchBody
                : path.StartsWith("/api/types/") ? "[]"
                : detail;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
