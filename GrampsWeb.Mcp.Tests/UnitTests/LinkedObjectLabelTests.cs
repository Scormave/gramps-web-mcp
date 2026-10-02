using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class LinkedObjectLabelTests
{
    [Fact]
    public async Task NoteCard_Names_Its_Tags_And_The_Objects_That_Reference_It()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/tags/t1"] = """{"handle":"t1","name":"To check"}""",
            // p2 is gone: its handle stays bare.
            ["/api/people/?handles=p1,p2&profile=self&page=1&pagesize=2"] = """
                [{"handle":"p1","profile":{"name_display":"Ivanov, Pyotr",
                  "birth":{"type":"Birth","date":"1880","place_name":"Tver"}}}]
                """,
            ["/api/events/e1?profile=self"] = """
                {"handle":"e1","type":"Birth","date":{"modifier":0,"dateval":[1,8,1856,false]},
                 "profile":{"place_name":"Tver"}}
                """,
        });
        var note = JsonSerializer.Deserialize<GrampsNote>(
            """{"handle":"n1","gramps_id":"N0001","type":"General","text":"Born at home","tag_list":["t1"]}""",
            GrampsJson.Options)!;
        IReadOnlyList<BacklinkGroup> backlinks =
        [
            new("people", "people", ["p1", "p2"]),
            new("events", "events", ["e1"]),
        ];

        var result = (await NoteFormatter.FormatNoteFullAsync(note, CreateClient(handler), backlinks))
            .Replace("\r\n", "\n");

        Assert.Contains("Tags (1):\n  • To check [handle: t1]\n", result);
        Assert.Contains(
            "  • Ivanov, Pyotr, b. 1880 in Tver [handle: p1]\n" +
            "  • person [handle: p2]\n",
            result);
        Assert.Contains("  • Birth — 1 Aug 1856 — Tver [handle: e1]\n", result);
        Assert.Equal(
            [
                "/api/events/e1?profile=self",
                "/api/people/?handles=p1,p2&profile=self&page=1&pagesize=2",
                "/api/tags/t1",
            ],
            handler.Requests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SourceCard_Names_Its_Repositories_Notes_And_Citations()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/repositories/r1"] = """{"handle":"r1","name":"State Archive","type":"Archive"}""",
            ["/api/notes/n1"] = """{"handle":"n1","type":"General","text":"Line one\nLine two"}""",
            ["/api/citations/c1?profile=self"] = """
                {"handle":"c1","page":"12","confidence":2,"profile":{"source":{"title":"Metrical book"}}}
                """,
        });
        var source = JsonSerializer.Deserialize<GrampsSource>(
            """
            {"handle":"s1","gramps_id":"S0001","title":"Metrical book",
             "reporef_list":[{"ref":"r1","call_number":"F. 1"}],"note_list":["n1"]}
            """,
            GrampsJson.Options)!;

        var result = (await SourceFormatter.FormatSourceFull(
                source, CreateClient(handler), [new("citations", "citations", ["c1"])]))
            .Replace("\r\n", "\n");

        Assert.Contains("Repositories (1):\n  • State Archive (Archive) [handle: r1] — call #: F. 1\n", result);
        Assert.Contains("Notes (1):\n  • [General] Line one Line two [handle: n1]\n", result);
        Assert.Contains("  • Metrical book — p. 12 (confidence: Normal) [handle: c1]\n", result);
    }

    [Fact]
    public async Task EventCard_Names_Its_Citations_In_One_Request()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/citations/?handles=c1,c2&profile=self&page=1&pagesize=2"] = """
                [{"handle":"c1","page":"12","confidence":3,"profile":{"source":{"title":"Metrical book"}}},
                 {"handle":"c2","confidence":2,"profile":{"source":{"title":"Census 1897"}}}]
                """,
        });
        var evt = JsonSerializer.Deserialize<GrampsEvent>(
            """{"handle":"e1","gramps_id":"E0001","type":"Birth","citation_list":["c1","c2"]}""",
            GrampsJson.Options)!;

        var result = (await EventFormatter.FormatEventFull(evt, CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains(
            "Citations (2):\n" +
            "  • Metrical book — p. 12 (confidence: High) [handle: c1]\n" +
            "  • Census 1897 (confidence: Normal) [handle: c2]\n",
            result);
        Assert.Equal(["/api/citations/?handles=c1,c2&profile=self&page=1&pagesize=2"], handler.Requests);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var http = new HttpClient(handler, disposeHandler: false);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance,
            new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance));
    }

    /// <summary>Serves bodies by path and query, empty type lists, and 404 otherwise.</summary>
    private sealed class PathHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
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
            return Task.FromResult(bodies.TryGetValue(path, out var body)
                ? Json(body)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
