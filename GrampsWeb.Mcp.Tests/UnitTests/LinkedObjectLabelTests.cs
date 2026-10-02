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
        Assert.Contains("  • Birth — 1856-08-01 — Tver [handle: e1]\n", result);
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

    [Fact]
    public async Task PersonCard_Names_Its_Families_Events_Media_Notes_Citations_And_Associates()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            // The Birth header line reads the event on its own.
            ["/api/events/e1"] = """{"handle":"e1","type":"Birth","date":{"modifier":0,"dateval":[1,8,1856,false]}}""",
            ["/api/tags/t1"] = """{"handle":"t1","name":"To check"}""",
            ["/api/families/?handles=f1,f2&profile=self&page=1&pagesize=2"] = """
                [{"handle":"f1","profile":{"father":{"name_display":"Ivanov, Ivan"},"mother":{"name_display":"Ivanova, Maria"}}},
                 {"handle":"f2","profile":{"mother":{"name_display":"Petrova, Anna"}}}]
                """,
            ["/api/events/?handles=e1,e2&profile=self&page=1&pagesize=2"] = """
                [{"handle":"e1","type":"Birth","date":{"modifier":0,"dateval":[1,8,1856,false]},"profile":{"place_name":"Tver"}},
                 {"handle":"e2","type":"Baptism","date":{"modifier":0,"dateval":[3,8,1856,false]}}]
                """,
            ["/api/media/m1"] = """{"handle":"m1","path":"scans/portrait.jpg","mime":"image/jpeg","desc":"Portrait"}""",
            ["/api/notes/n1"] = """{"handle":"n1","type":"Research","text":"Check the census"}""",
            ["/api/citations/c1?profile=self"] = """
                {"handle":"c1","page":"12","confidence":2,"profile":{"source":{"title":"Metrical book"}}}
                """,
            // p3 is gone: the association keeps its generic wording.
            ["/api/people/?handles=p2,p3&profile=self&page=1&pagesize=2"] = """
                [{"handle":"p2","profile":{"name_display":"Sidorov, Fyodor"}}]
                """,
        });
        var person = JsonSerializer.Deserialize<GrampsPerson>(
            """
            {"handle":"p1","gramps_id":"I0001","gender":1,
             "primary_name":{"first_name":"Pyotr","surname_list":[{"surname":"Ivanov"}]},
             "tag_list":["t1"],
             "parent_family_list":["f1"],"family_list":["f2"],
             "event_ref_list":[{"ref":"e1","role":"Primary"},{"ref":"e2","role":"Godparent"}],
             "media_list":[{"ref":"m1"}],"note_list":["n1"],"citation_list":["c1"],
             "person_ref_list":[{"ref":"p2","rel":"Godfather"},{"ref":"p3","rel":"Witness"}]}
            """,
            GrampsJson.Options)!;

        var result = (await PersonFormatter.FormatPersonFull(person, CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains("Tags (1):\n  • To check [handle: t1]\n", result);
        Assert.Contains(
            "  Parent families (as child) (1):\n  • Ivanov, Ivan and Ivanova, Maria [handle: f1]\n", result);
        Assert.Contains("  Families as parent or spouse (1):\n  • Petrova, Anna [handle: f2]\n", result);
        Assert.Contains(
            "Events (2):\n" +
            "  • Birth — 1856-08-01 — Tver [handle: e1] role: Primary\n" +
            "  • Baptism — 1856-08-03 [handle: e2] role: Godparent\n",
            result);
        Assert.Contains("Gallery (media) (1):\n  • [image] Portrait (portrait.jpg) [handle: m1]\n", result);
        Assert.Contains("Notes (1):\n  • [Research] Check the census [handle: n1]\n", result);
        Assert.Contains(
            "Sources (citations) (1):\n  • Metrical book — p. 12 (confidence: Normal) [handle: c1]\n", result);
        Assert.Contains(
            "  • Sidorov, Fyodor [handle: p2] — Godfather\n" +
            "  • related person [handle: p3] — Witness\n",
            result);
        Assert.Equal(
            [
                "/api/citations/c1?profile=self",
                "/api/events/?handles=e1,e2&profile=self&page=1&pagesize=2",
                "/api/events/e1",
                "/api/families/?handles=f1,f2&profile=self&page=1&pagesize=2",
                "/api/media/m1",
                "/api/notes/n1",
                "/api/people/?handles=p2,p3&profile=self&page=1&pagesize=2",
                "/api/tags/t1",
            ],
            handler.Requests.Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FamilyCard_Names_Its_Tags_Media_Notes_And_Citations()
    {
        var handler = new PathHandler(new Dictionary<string, string>
        {
            ["/api/tags/t1"] = """{"handle":"t1","name":"To check"}""",
            // m2 is gone: its handle stays bare.
            ["/api/media/?handles=m1,m2&page=1&pagesize=2"] = """
                [{"handle":"m1","path":"scans/wedding.jpg","mime":"image/jpeg"}]
                """,
            ["/api/notes/n1"] = """{"handle":"n1","type":"General","text":"Married in Tver"}""",
            ["/api/citations/c1?profile=self"] = """
                {"handle":"c1","page":"7","confidence":3,"profile":{"source":{"title":"Marriage register"}}}
                """,
        });
        var family = JsonSerializer.Deserialize<GrampsFamily>(
            """
            {"handle":"f1","gramps_id":"F0001","father_handle":"p1",
             "profile":{"father":{"handle":"p1","name_display":"Ivanov, Pyotr"}},
             "tag_list":["t1"],"media_list":[{"ref":"m1"},{"ref":"m2"}],
             "note_list":["n1"],"citation_list":["c1"]}
            """,
            GrampsJson.Options)!;

        var result = (await FamilyFormatter.FormatFamilyFullAsync(family, CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains("Father: Ivanov, Pyotr", result);
        Assert.Contains("Tags (1):\n  • To check [handle: t1]\n", result);
        Assert.Contains(
            "Gallery (media) (2):\n" +
            "  • [image] wedding.jpg [handle: m1]\n" +
            "  • [handle: m2]\n",
            result);
        Assert.Contains("Notes (1):\n  • [General] Married in Tver [handle: n1]\n", result);
        Assert.Contains(
            "Sources (citations) (1):\n  • Marriage register — p. 7 (confidence: High) [handle: c1]\n", result);
        Assert.Equal(
            [
                "/api/citations/c1?profile=self",
                "/api/media/?handles=m1,m2&page=1&pagesize=2",
                "/api/notes/n1",
                "/api/tags/t1",
            ],
            handler.Requests.Order(StringComparer.Ordinal));
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
