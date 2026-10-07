using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>The <c>sortEvents</c> argument of update_person and update_family.</summary>
public class EventRefOrderTests
{
    [Fact]
    public async Task UpdatePerson_SortEvents_Orders_Links_By_Date_And_Keeps_Their_Metadata()
    {
        var marriageSortVal = GrampsDateSortVal.SortValueOf(GrampsCalendars.Gregorian, 0, 1866, 11, 11);
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """
                {
                  "handle": "h-person", "gramps_id": "I0100",
                  "event_ref_list": [
                    { "_class": "EventRef", "ref": "e-burial", "role": "Primary" },
                    { "_class": "EventRef", "ref": "e-occupation", "role": "Primary" },
                    { "_class": "EventRef", "ref": "e-marriage", "role": "Witness", "private": true, "citation_list": ["c1"] },
                    { "_class": "EventRef", "ref": "e-death", "role": "Primary" },
                    { "_class": "EventRef", "ref": "e-baptism", "role": "Primary" },
                    { "_class": "EventRef", "ref": "e-residence", "role": "Primary", "note_list": ["n1"] },
                    { "_class": "EventRef", "ref": "e-birth", "role": "Primary" },
                    { "_class": "EventRef", "ref": "e-missing", "role": "Primary" }
                  ],
                  "birth_ref_index": 6
                }
                """,
            ["/api/events/?handles=e-burial,e-occupation,e-marriage,e-death,e-baptism,e-residence,e-birth,e-missing&page=1&pagesize=8"] = $$"""
                [
                  { "handle": "e-burial", "type": "Burial", "date": { "modifier": 2, "dateval": [1, 5, 1890, false] } },
                  { "handle": "e-occupation", "type": "Occupation", "date": { "modifier": 0, "dateval": [0, 0, 0, false], "sortval": 0 } },
                  { "handle": "e-marriage", "type": "Marriage", "date": { "dateval": [11, 11, 1866, false], "sortval": {{marriageSortVal}} } },
                  { "handle": "e-death", "type": "Death", "date": { "dateval": [1, 5, 1890, false] } },
                  { "handle": "e-baptism", "type": "Baptism", "date": { "calendar": 1, "dateval": [13, 3, 1857, false] } },
                  { "handle": "e-residence", "type": { "_class": "EventType", "string": "Residence" }, "date": { "dateval": [11, 11, 1866, false] } },
                  { "handle": "e-birth", "type": "Birth", "date": { "modifier": 1, "dateval": [25, 3, 1857, false] } }
                ]
                """
        });
        var stored = JsonNode.Parse(handler.Bodies["/api/people/h-person"])!;

        await PersonTools.UpdatePerson("h-person", client: CreateClient(handler), sortEvents: true);

        var refs = handler.Body!["event_ref_list"]!.AsArray();
        Assert.Equal(
            ["e-birth", "e-baptism", "e-residence", "e-marriage", "e-death", "e-burial", "e-occupation", "e-missing"],
            refs.Select(r => r!["ref"]!.GetValue<string>()));
        AssertJsonEqual(stored["event_ref_list"]![2], refs[3]);
        AssertJsonEqual(stored["event_ref_list"]![5], refs[2]);
        Assert.Equal("/api/people/h-person", handler.PutPath);
    }

    [Fact]
    public async Task UpdateFamily_SortEvents_Places_An_Added_Link_By_Its_Date()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                {
                  "handle": "h-family", "gramps_id": "F0100",
                  "event_ref_list": [
                    { "_class": "EventRef", "ref": "e-marriage", "role": "Family" },
                    { "_class": "EventRef", "ref": "e-divorce", "role": "Family" }
                  ]
                }
                """,
            ["/api/types/default/"] = """{ "event_role_types": ["Primary", "Family"] }""",
            ["/api/events/?handles=e-marriage,e-divorce,e-banns&page=1&pagesize=3"] = """
                [
                  { "handle": "e-marriage", "type": "Marriage", "date": { "dateval": [11, 11, 1866, false] } },
                  { "handle": "e-divorce", "type": "Divorce", "date": { "dateval": [0, 0, 1900, false] } },
                  { "handle": "e-banns", "type": "Marriage Banns", "date": { "dateval": [20, 10, 1866, false] } }
                ]
                """
        });

        await FamilyTools.UpdateFamily("h-family",
            eventRefs: JsonSerializer.Deserialize<FlexibleEventRefList>("""["e-banns::Family"]"""),
            client: CreateClient(handler), linkMode: "add", sortEvents: true);

        Assert.Equal(
            ["e-banns", "e-marriage", "e-divorce"],
            handler.Body!["event_ref_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdatePerson_Without_SortEvents_Does_Not_Read_Events()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """
                {
                  "handle": "h-person", "gramps_id": "I0100",
                  "event_ref_list": [{ "ref": "e-death", "role": "Primary" }, { "ref": "e-birth", "role": "Primary" }]
                }
                """
        });

        await PersonTools.UpdatePerson("h-person", gender: "male", client: CreateClient(handler));

        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/events/", StringComparison.Ordinal));
        Assert.Equal(
            ["e-death", "e-birth"],
            handler.Body!["event_ref_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));
    }

    [Fact]
    public void SortKey_Puts_Undated_Births_First_And_Undated_Deaths_Last()
    {
        GrampsEvent Undated(string type) => new() { Type = type, Date = new GrampsDate() };
        var dated = new GrampsEvent { Type = "Census", Date = GrampsDate.YearOnly(1900) };
        var events = new[]
        {
            Undated("Burial"), Undated("Death"), Undated("Census"), dated, Undated("Baptism"), Undated("Birth"), null
        };

        var sorted = events.OrderBy(EventRefOrder.SortKey).ToArray();

        Assert.Equal(
            [events[5], events[4], dated, events[2], events[6], events[1], events[0]],
            sorted);
    }

    [Fact]
    public void SortKey_Treats_A_Text_Only_Date_As_Undated()
    {
        var textOnly = new GrampsEvent { Type = "Census", Date = new GrampsDate { Modifier = 6, Text = "in his youth", Year = 1900 } };

        Assert.Equal(2, EventRefOrder.SortKey(textOnly).Position);
    }

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected:\n{expected}\nActual:\n{actual}");

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://event-order.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Serves bodies by path and query (404 otherwise) and records the PUT body.</summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyDictionary<string, string> Bodies => bodies;

        public IReadOnlyList<string> Requests => [.. _requests];

        public JsonNode? Body { get; private set; }

        public string? PutPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Put)
            {
                PutPath = request.RequestUri.AbsolutePath;
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json("{}");
            }

            return bodies.TryGetValue(path, out var body) ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
