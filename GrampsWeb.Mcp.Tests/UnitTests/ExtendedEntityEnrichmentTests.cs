using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ExtendedEntityEnrichmentTests
{
    // e2 has no place, e3 came with its place, and c2 with its source: only e1, e4, c1 and the media are fetched.
    private const string Person = """
        {
          "handle": "p1",
          "media_list": [{ "ref": "m2" }, { "ref": "m1" }],
          "extended": {
            "events": [
              { "handle": "e1", "place": "pl1" },
              { "handle": "e2", "place": "" },
              { "handle": "e3", "place": "pl3", "extended": { "place": { "handle": "pl3" } } },
              { "handle": "e4", "place": "pl4" }
            ],
            "citations": [
              { "handle": "c1", "source_handle": "s1" },
              { "handle": "c2", "source_handle": "s2", "extended": { "source": { "handle": "s2" } } }
            ]
          }
        }
        """;

    [Fact]
    public async Task EnrichPersonExtendedAsync_Fetches_Each_Object_Type_In_One_Request()
    {
        var handler = new EnrichmentHandler(Server);
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>(Person, GrampsJson.Options)!;

        await ExtendedEntityEnrichment.EnrichPersonExtendedAsync(person, CreateClient(handler));

        Assert.Equal(
            [
                "/api/citations/c1?extend=all",
                "/api/events/?handles=e1,e4&extend=place&page=1&pagesize=2",
                "/api/media/?handles=m2,m1&page=1&pagesize=2"
            ],
            handler.Requests.Order());
        var ext = person.Extended!;
        Assert.Equal(["e1", "e2", "e3", "e4"], ext.Events!.Select(e => e.Handle));
        Assert.Equal(["pl1", null, "pl3", "pl4"], ext.Events!.Select(e => e.Extended?.Place?.Handle));
        Assert.Equal(["s1", "s2"], ext.Citations!.Select(c => c.Extended?.Source?.Handle));
        Assert.Equal(["m2", "m1"], ext.Media!.Select(m => m.Handle));
    }

    [Fact]
    public async Task EnrichFamilyExtendedAsync_Batches_Events_And_Keeps_Extended_Media()
    {
        var handler = new EnrichmentHandler(Server);
        var family = JsonSerializer.Deserialize<GrampsFamilyExtended>("""
            {
              "handle": "f1",
              "media_list": [{ "ref": "m1" }],
              "extended": {
                "events": [{ "handle": "e1", "place": "pl1" }, { "handle": "e4", "place": "pl4" }],
                "media": [{ "handle": "m1" }]
              }
            }
            """, GrampsJson.Options)!;

        await ExtendedEntityEnrichment.EnrichFamilyExtendedAsync(family, CreateClient(handler));

        Assert.Equal(["/api/events/?handles=e1,e4&extend=place&page=1&pagesize=2"], handler.Requests);
        Assert.Equal(["pl1", "pl4"], family.Extended!.Events!.Select(e => e.Extended?.Place?.Handle));
        Assert.Equal("m1", Assert.Single(family.Extended.Media!).Handle);
    }

    [Fact]
    public async Task EnrichPersonExtendedAsync_Keeps_The_Rows_When_A_Request_Fails()
    {
        var handler = new EnrichmentHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>(Person, GrampsJson.Options)!;

        await ExtendedEntityEnrichment.EnrichPersonExtendedAsync(person, CreateClient(handler));

        var ext = person.Extended!;
        Assert.Equal(["e1", "e2", "e3", "e4"], ext.Events!.Select(e => e.Handle));
        Assert.Equal([null, null, "pl3", null], ext.Events!.Select(e => e.Extended?.Place?.Handle));
        Assert.Equal([null, "s2"], ext.Citations!.Select(c => c.Extended?.Source?.Handle));
        Assert.Empty(ext.Media!);
    }

    /// <summary>Gramps Web API 3.14+: list routes honor <c>handles</c> and objects come back extended.</summary>
    private static HttpResponseMessage Server(Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        var collection = segments[1];
        var handles = segments.Length > 2
            ? [segments[2]]
            : HttpUtility.ParseQueryString(uri.Query)["handles"]!.Split(',');
        var objects = handles.Select(h => collection switch
        {
            "events" => $$"""{ "handle": "{{h}}", "place": "pl{{h[1..]}}", "extended": { "place": { "handle": "pl{{h[1..]}}" } } }""",
            "citations" => $$"""{ "handle": "{{h}}", "source_handle": "s{{h[1..]}}", "extended": { "source": { "handle": "s{{h[1..]}}" } } }""",
            _ => $$"""{ "handle": "{{h}}" }"""
        }).ToList();
        return Json(segments.Length > 2 ? objects[0] : "[" + string.Join(",", objects) + "]");
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://enrichment.test", "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri(config.ApiUrl) },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class EnrichmentHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
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
