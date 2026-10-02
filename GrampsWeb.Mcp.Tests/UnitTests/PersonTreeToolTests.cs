using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class PersonTreeToolTests
{
    [Fact]
    public async Task GetPersonTree_Ancestors_Loads_Each_Generation_In_Batches()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/me"] = """{ "handle": "me", "gramps_id": "I0001", "parent_family_list": [{ "ref": "fam-p" }] }""",
            ["/api/families/fam-p"] = """
                { "handle": "fam-p", "father_handle": "dad", "mother_handle": "mom", "child_ref_list": [{ "ref": "me" }] }
                """,
            // Mom's parent family is missing from her parent_family_list; her backlinks still name it.
            // She has no surname, and Gramps keeps the separator in name_display.
            ["/api/people/?handles=dad,mom&profile=self&backlinks=true&page=1&pagesize=2"] = """
                [
                  { "handle": "dad", "gramps_id": "I0002", "gender": 1, "parent_family_list": [{ "ref": "fam-gp" }],
                    "backlinks": { "family": ["fam-p"] },
                    "profile": { "name_display": "Ivanov, Pyotr", "birth": { "type": "Birth", "date": "1950", "place_name": "Tver" }, "death": {} } },
                  { "handle": "mom", "gramps_id": "I0003", "gender": 0, "parent_family_list": [],
                    "backlinks": { "family": ["fam-p", "fam-mp"] },
                    "profile": { "name_display": ", Olga", "birth": { "type": "Baptism", "date": "1952" }, "death": {} } }
                ]
                """,
            ["/api/families/?handles=fam-gp,fam-mp&page=1&pagesize=2"] = """
                [
                  { "handle": "fam-gp", "father_handle": "gf", "mother_handle": "", "child_ref_list": [{ "ref": "dad" }] },
                  { "handle": "fam-mp", "father_handle": "mgf", "mother_handle": "mgm", "child_ref_list": [{ "ref": "mom" }] }
                ]
                """,
            // The last generation needs no backlinks; mgm no longer exists.
            ["/api/people/?handles=gf,mgf,mgm&profile=self&page=1&pagesize=3"] = """
                [
                  { "handle": "gf", "gramps_id": "I0004", "profile": { "name_display": "Ivanov, Ivan", "birth": {}, "death": { "type": "Death", "date": "1990" } } },
                  { "handle": "mgf", "gramps_id": "I0005", "profile": { "name_display": "", "birth": {}, "death": {} } }
                ]
                """,
        });

        var result = (await PersonTools.GetPersonTree("me", "ancestors", generations: 2, client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Equal(
            """
            ANCESTOR TREE [root: me]
            ============================================================
            Total: 4

              1. Gen 1 — Father
                 Ivanov, Pyotr, b. 1950 in Tver [I0002]
                 [handle: dad] (gramps_id: I0002)
              2. Gen 1 — Mother
                 Olga, baptism 1952 [I0003]
                 [handle: mom] (gramps_id: I0003)
              3. Gen 2 — Father's father
                 Ivanov, Ivan, d. 1990 [I0004]
                 [handle: gf] (gramps_id: I0004)
              4. Gen 2 — Mother's father
                 Unknown [I0005]
                 [handle: mgf] (gramps_id: I0005)

            """.Replace("\r\n", "\n"),
            result);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPersonTree_Root_Without_Parent_Families_Checks_Its_Backlinks()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/me"] = """{ "handle": "me", "gramps_id": "I0001" }""",
            ["/api/people/me?backlinks=true"] = """{ "handle": "me", "backlinks": { "family": ["fam-own", "fam-p"] } }""",
            // fam-own lists me as a parent, not as a child, so it is not a parent family.
            ["/api/families/?handles=fam-own,fam-p&page=1&pagesize=2"] = """
                [
                  { "handle": "fam-own", "father_handle": "me", "mother_handle": "wife", "child_ref_list": [{ "ref": "son" }] },
                  { "handle": "fam-p", "father_handle": "dad", "mother_handle": "", "child_ref_list": [{ "ref": "me" }] }
                ]
                """,
            ["/api/people/dad?profile=self"] = """
                { "handle": "dad", "gramps_id": "I0002", "profile": { "name_display": "Ivanov, Pyotr", "birth": {}, "death": {} } }
                """,
        });

        var result = await PersonTools.GetPersonTree("me", "ancestors", generations: 1, client: CreateClient(handler));

        Assert.Contains("Total: 1\n", result.Replace("\r\n", "\n"));
        Assert.Contains("1. Gen 1 — Father", result);
        Assert.DoesNotContain("wife", result);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetPersonTree_Descendants_Loads_Each_Generation_In_Batches()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/me"] = """{ "handle": "me", "gramps_id": "I0001", "family_list": ["fam-a", "fam-b"] }""",
            ["/api/families/?handles=fam-a,fam-b&page=1&pagesize=2"] = """
                [
                  { "handle": "fam-a", "child_ref_list": [{ "ref": "kid1" }, { "ref": "kid2" }] },
                  { "handle": "fam-b", "child_ref_list": [{ "ref": "kid2" }, { "ref": "kid3" }] }
                ]
                """,
            ["/api/people/?handles=kid1,kid2,kid3&profile=self&page=1&pagesize=3"] = """
                [
                  { "handle": "kid3", "gramps_id": "I0004", "gender": 2, "profile": { "name_display": "C", "birth": {}, "death": {} } },
                  { "handle": "kid1", "gramps_id": "I0002", "gender": 1, "family_list": ["fam-k"],
                    "profile": { "name_display": "A", "birth": { "date": "1980" }, "death": {} } },
                  { "handle": "kid2", "gramps_id": "I0003", "gender": 0, "profile": { "name_display": "B", "birth": {}, "death": {} } }
                ]
                """,
            ["/api/families/fam-k"] = """{ "handle": "fam-k", "child_ref_list": [{ "ref": "gk" }] }""",
            ["/api/people/gk?profile=self"] = """
                { "handle": "gk", "gramps_id": "I0005", "gender": 0, "profile": { "name_display": "D", "birth": {}, "death": {} } }
                """,
        });

        var result = (await PersonTools.GetPersonTree("me", "descendants", generations: 2, client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        // Rows keep family and child order, not the order of the batch reply.
        Assert.Contains(
            "  1. Gen 1 — Son\n     A, b. 1980 [I0002]\n" +
            "     [handle: kid1] (gramps_id: I0002)\n" +
            "  2. Gen 1 — Daughter\n     B [I0003]\n" +
            "     [handle: kid2] (gramps_id: I0003)\n" +
            "  3. Gen 1 — Child\n     C [I0004]\n" +
            "     [handle: kid3] (gramps_id: I0004)\n" +
            "  4. Gen 2 — Granddaughter\n     D [I0005]\n",
            result);
        Assert.Equal(5, handler.Requests.Count);
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

    /// <summary>Serves bodies by path and query, 404 otherwise; requests may arrive in parallel.</summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
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
