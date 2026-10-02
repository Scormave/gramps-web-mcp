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
public class RelationsToolTests
{
    private const string Anna = """
        { "handle": "anna-h", "gramps_id": "I0001", "profile": {
            "handle": "anna-h", "gramps_id": "I0001", "name_display": "Ivanova, Anna",
            "birth": { "type": "Birth", "date": "1950", "place_name": "Tver" }, "death": {} } }
        """;

    private const string Boris = """
        { "handle": "boris-h", "gramps_id": "I0002", "profile": {
            "handle": "boris-h", "gramps_id": "I0002", "name_display": "Petrov, Boris",
            "birth": { "type": "Birth", "date": "1952", "place_name": "" }, "death": {} } }
        """;

    private const string RelationsPath = "/api/relations/anna-h/boris-h";

    [Fact]
    public async Task GetRelations_Names_People_And_Common_Ancestors_Of_Every_Relationship()
    {
        var handler = new RelationsHandler(new Dictionary<string, string>
        {
            [Person("anna-h")] = Anna,
            [Person("boris-h")] = Boris,
            [RelationsPath] = """{ "relationship_string": "first cousin", "distance_common_origin": 2, "distance_common_other": 2 }""",
            [RelationsPath + "/all"] = """
                [
                  { "relationship_string": "first cousin", "common_ancestors": ["gf-h", "gm-h"] },
                  { "relationship_string": "second cousin once removed", "common_ancestors": ["gf-h", "gone-h", "gf-h"] }
                ]
                """,
            // One batch for every common ancestor; the server skips the missing gone-h.
            ["/api/people/?handles=gf-h,gm-h,gone-h&profile=self&page=1&pagesize=3"] = """
                [
                  { "handle": "gf-h", "profile": { "handle": "gf-h", "gramps_id": "I0010", "name_display": "Ivanov, Pyotr",
                    "birth": { "date": "1890" }, "death": {} } },
                  { "handle": "gm-h", "profile": { "handle": "gm-h", "gramps_id": "I0011" } }
                ]
                """,
        });

        var result = (await PersonTools.GetRelations("anna-h", "boris-h", CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Equal(
            """
            RELATIONSHIP
            ============================================================
            Person 1: Ivanova, Anna (I0001) [handle: anna-h], b. 1950 in Tver
            Person 2: Petrov, Boris (I0002) [handle: boris-h], b. 1952

            Petrov, Boris (I0002) is the first cousin of Ivanova, Anna (I0001).
            Generations to the common ancestor: 2 from person 1, 2 from person 2.

            All relationships (2), closest first:
              1. first cousin
                 Common ancestors:
                   - Ivanov, Pyotr (I0010) [handle: gf-h], b. 1890
                   - I0011 [handle: gm-h]
              2. second cousin once removed
                 Common ancestors:
                   - Ivanov, Pyotr (I0010) [handle: gf-h], b. 1890
                   - [handle: gone-h]

            """.Replace("\r\n", "\n"),
            result);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task GetRelations_Direct_Ancestor_Reuses_The_Fetched_Person()
    {
        var handler = new RelationsHandler(new Dictionary<string, string>
        {
            [Person("anna-h")] = Anna,
            [Person("boris-h")] = Boris,
            [RelationsPath] = """{ "relationship_string": "son", "distance_common_origin": 0, "distance_common_other": 1 }""",
            [RelationsPath + "/all"] = """[{ "relationship_string": "son", "common_ancestors": ["anna-h"] }]""",
        });

        var result = (await PersonTools.GetRelations("anna-h", "boris-h", CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains(
            "Petrov, Boris (I0002) is the son of Ivanova, Anna (I0001).\n" +
            "Generations to the common ancestor: 0 from person 1, 1 from person 2.\n" +
            "\n" +
            "Common ancestors:\n" +
            "  - Ivanova, Anna (I0001) [handle: anna-h], b. 1950 in Tver\n",
            result);
        Assert.DoesNotContain("All relationships", result);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetRelations_Spouses_Have_No_Generations_Or_Ancestors()
    {
        var handler = new RelationsHandler(new Dictionary<string, string>
        {
            [Person("anna-h")] = Anna,
            [Person("boris-h")] = Boris,
            [RelationsPath] = """{ "relationship_string": "husband", "distance_common_origin": -1, "distance_common_other": -1 }""",
            [RelationsPath + "/all"] = """[{ "relationship_string": "husband", "common_ancestors": [] }]""",
        });

        var result = await PersonTools.GetRelations("anna-h", "boris-h", CreateClient(handler));

        Assert.Contains("Petrov, Boris (I0002) is the husband of Ivanova, Anna (I0001).", result);
        Assert.DoesNotContain("Generations", result);
        Assert.DoesNotContain("Common ancestors", result);
        Assert.DoesNotContain("All relationships", result);
    }

    [Fact]
    public async Task GetRelations_Unrelated_People_Get_A_Clear_Message()
    {
        var handler = new RelationsHandler(new Dictionary<string, string>
        {
            [Person("anna-h")] = Anna,
            [Person("boris-h")] = Boris,
            [RelationsPath] = """{ "relationship_string": "", "distance_common_origin": -1, "distance_common_other": -1 }""",
            [RelationsPath + "/all"] = "[{}]",
        });

        var result = await PersonTools.GetRelations("anna-h", "boris-h", CreateClient(handler));

        Assert.Contains("Person 2: Petrov, Boris (I0002) [handle: boris-h], b. 1952", result);
        Assert.Contains("Not related: they are not spouses and share no ancestor within 15 generations.", result);
        Assert.DoesNotContain(" is the ", result);
    }

    [Fact]
    public async Task GetRelations_Same_Person_Skips_The_Calculation()
    {
        var handler = new RelationsHandler(new Dictionary<string, string> { [Person("anna-h")] = Anna });

        var result = await PersonTools.GetRelations("anna-h", "anna-h", CreateClient(handler));

        Assert.Equal("Ivanova, Anna (I0001) [handle: anna-h], b. 1950 in Tver: both handles refer to the same person.", result);
        Assert.Equal([Person("anna-h")], handler.Requests);
    }

    [Fact]
    public async Task GetRelations_Reports_The_Missing_Person()
    {
        var handler = new RelationsHandler(new Dictionary<string, string> { [Person("anna-h")] = Anna });

        var result = await PersonTools.GetRelations("anna-h", "boris-h", CreateClient(handler));

        Assert.StartsWith("Person not found: boris-h", result);
    }

    private static string Person(string handle) => $"/api/people/{handle}?profile=self";

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
    private sealed class RelationsHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
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
