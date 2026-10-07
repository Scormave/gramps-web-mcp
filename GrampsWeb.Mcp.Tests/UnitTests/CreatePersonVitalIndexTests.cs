using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>create_person sets birth_ref_index and death_ref_index, which Gramps Web sets only on PUT.</summary>
public class CreatePersonVitalIndexTests
{
    [Fact]
    public async Task CreatePerson_Points_The_Indexes_At_The_First_Primary_Birth_And_Death()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/events/?handles=e-baptism,e-birth,e-death,e-birth-2&page=1&pagesize=4"] = """
                [
                  { "handle": "e-baptism", "type": "Baptism" },
                  { "handle": "e-birth", "type": "Birth" },
                  { "handle": "e-death", "type": "Death" },
                  { "handle": "e-birth-2", "type": "Birth" }
                ]
                """
        });

        await PersonTools.CreatePerson(
            Name(),
            eventRefs: JsonSerializer.Deserialize<FlexibleEventRefList>(
                """["e-baptism", "e-birth", "e-son-death::Witness", "e-death", "e-birth-2"]"""),
            client: CreateClient(handler));

        Assert.Equal(1, handler.PostBody!["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(3, handler.PostBody["death_ref_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task CreatePerson_Counts_Every_Link_And_Skips_A_Birth_In_Another_Role()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/events/e-birth"] = """{ "handle": "e-birth", "type": "Birth" }"""
        });

        await PersonTools.CreatePerson(
            Name(),
            eventRefs: JsonSerializer.Deserialize<FlexibleEventRefList>("""["e-child-birth::Father", "e-birth"]"""),
            client: CreateClient(handler));

        Assert.Equal(["/api/events/e-birth"], handler.Requests.Where(p => p.StartsWith("/api/events/", StringComparison.Ordinal)));
        Assert.Equal(1, handler.PostBody!["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(-1, handler.PostBody["death_ref_index"]!.GetValue<int>());
    }

    [Fact]
    public async Task CreatePerson_Without_Primary_Events_Sends_Minus_One_Without_Reading_Events()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await PersonTools.CreatePerson(
            Name(),
            eventRefs: JsonSerializer.Deserialize<FlexibleEventRefList>("""["e-child-birth::Father"]"""),
            client: CreateClient(handler));

        Assert.DoesNotContain(handler.Requests, p => p.StartsWith("/api/events/", StringComparison.Ordinal));
        Assert.Equal(-1, handler.PostBody!["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(-1, handler.PostBody["death_ref_index"]!.GetValue<int>());
    }

    private static FlexibleGrampsName Name() =>
        JsonSerializer.Deserialize<FlexibleGrampsName>("""{ "given": "Ivan", "surname": "Petrov" }""")!;

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://create-person-vitals.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Serves the role vocabulary and bodies by path and query (404 otherwise) and records the POST body.</summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public JsonNode? PostBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Post)
            {
                PostBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json("""[{ "_class": "Person", "type": "add", "old": null, "new": { "_class": "Person", "handle": "h-new", "gramps_id": "I0001" } }]""");
            }

            if (path == "/api/types/default/")
                return Json("""{ "event_role_types": ["Primary", "Father", "Witness"] }""");
            return bodies.TryGetValue(path, out var body) ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
