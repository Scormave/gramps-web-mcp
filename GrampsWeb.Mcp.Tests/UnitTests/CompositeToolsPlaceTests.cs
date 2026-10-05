using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class CompositeToolsPlaceTests
{
    private const string PersonHandle = "person-handle-0001";

    public CompositeToolsPlaceTests()
    {
        HandleCache.Invalidate();
    }

    [Fact]
    public async Task AddEventToPerson_Finds_The_Place_Through_Gql_And_Matches_The_Whole_Name()
    {
        var handler = new CompositeHandler
        {
            PlaceList = """
                [
                  {"handle": "county-h", "gramps_id": "P0003", "name": {"value": "Warsaw County"}},
                  {"handle": "warsaw-h", "gramps_id": "P0004", "name": {"value": " warsaw "}}
                ]
                """
        };

        var result = await CompositeTools.AddEventToPerson(PersonHandle, "Birth", place: "Warsaw", client: CreateClient(handler));

        var query = HttpUtility.ParseQueryString(new Uri("https://composite.test" + handler.PlaceListPath).Query);
        Assert.Equal("name.value ~ \"Warsaw\"", query["gql"]);
        Assert.Equal("handle,gramps_id,name", query["keys"]);
        Assert.Null(query["page"]);
        Assert.DoesNotContain(handler.Mutations, m => m.Path == "/api/places/");
        Assert.Equal("warsaw-h", handler.EventBody!.Value.GetProperty("place").GetString());
        Assert.Contains("P0004 (handle: warsaw-h) [existing]", result);
    }

    [Fact]
    public async Task AddEventToPerson_Creates_The_Place_When_Only_A_Longer_Name_Contains_It()
    {
        var handler = new CompositeHandler
        {
            PlaceList = """[{"handle": "county-h", "gramps_id": "P0003", "name": {"value": "Warsaw County"}}]"""
        };

        var result = await CompositeTools.AddEventToPerson(PersonHandle, "Birth", place: "Warsaw", client: CreateClient(handler));

        var created = Assert.Single(handler.Mutations, m => m.Path == "/api/places/");
        Assert.Equal("Warsaw", created.Body.GetProperty("name").GetProperty("value").GetString());
        Assert.Equal("new-place-h", handler.EventBody!.Value.GetProperty("place").GetString());
        Assert.Contains("[created]", result);
    }

    [Fact]
    public async Task AddEventToPerson_Matches_A_Name_With_Double_Quotes_Against_Every_Place()
    {
        var handler = new CompositeHandler
        {
            PlaceList = """[{"handle": "inn-h", "gramps_id": "P0005", "name": {"value": "London \"Inn\""}}]"""
        };

        await CompositeTools.AddEventToPerson(PersonHandle, "Birth", place: "London \"Inn\"", client: CreateClient(handler));

        var query = HttpUtility.ParseQueryString(new Uri("https://composite.test" + handler.PlaceListPath).Query);
        Assert.Null(query["gql"]);
        Assert.Equal("inn-h", handler.EventBody!.Value.GetProperty("place").GetString());
    }

    [Fact]
    public async Task AddEventToPerson_Uses_The_Place_With_The_Gramps_Id()
    {
        var handler = new CompositeHandler();

        await CompositeTools.AddEventToPerson(PersonHandle, "Birth", place: "P0003", client: CreateClient(handler));

        Assert.Null(handler.PlaceListPath);
        Assert.Equal("stored-place-h", handler.EventBody!.Value.GetProperty("place").GetString());
    }

    [Theory]
    [InlineData("P0099", "No place has Gramps ID P0099")]
    [InlineData("I0005", "I0005 has the person prefix I")]
    [InlineData("0123456789abcdef0123", "Place not found: 0123456789abcdef0123")]
    public async Task AddEventToPerson_Reports_An_Unknown_Place_Id_Or_Handle_And_Creates_Nothing(
        string place, string expected)
    {
        var handler = new CompositeHandler();

        var result = await CompositeTools.AddEventToPerson(PersonHandle, "Birth", place: place, client: CreateClient(handler));

        Assert.StartsWith($"Place not found: {place}", result);
        Assert.Contains(expected, result);
        Assert.Null(handler.PlaceListPath);
        Assert.Empty(handler.Mutations);
    }

    [Fact]
    public async Task QuickAddPerson_Does_Not_Create_A_Place_When_The_Lookup_Fails()
    {
        var handler = new CompositeHandler { PlaceListStatus = HttpStatusCode.InternalServerError };

        await Assert.ThrowsAsync<McpException>(() =>
            CompositeTools.QuickAddPerson("John Smith", birthPlace: "London", client: CreateClient(handler)));

        Assert.NotNull(handler.PlaceListPath);
        Assert.Empty(handler.Mutations);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://composite.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers one person, the place routes and the mutations of the composite tools, and records
    /// the place list request and every mutation. The place P0003 is stored as <c>stored-place-h</c>.
    /// </summary>
    private sealed class CompositeHandler : HttpMessageHandler
    {
        public string PlaceList { get; init; } = "[]";

        public HttpStatusCode PlaceListStatus { get; init; } = HttpStatusCode.OK;

        public string? PlaceListPath { get; private set; }

        public List<(string Path, JsonElement Body)> Mutations { get; } = [];

        public JsonElement? EventBody =>
            Mutations.Where(m => m.Path == "/api/events/").Select(m => (JsonElement?)m.Body).FirstOrDefault();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;

            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                    return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
                Mutations.Add((path, body));
                return path switch
                {
                    "/api/places/" => Json("""[{"_class":"Place","type":"add","old":null,"new":{"_class":"Place","handle":"new-place-h","gramps_id":"P0100"}}]"""),
                    "/api/events/" => Json("""[{"_class":"Event","type":"add","old":null,"new":{"_class":"Event","handle":"event-h","gramps_id":"E0100"}}]"""),
                    "/api/people/" => Json("""[{"_class":"Person","type":"add","old":null,"new":{"_class":"Person","handle":"new-person-h","gramps_id":"I0100"}}]"""),
                    _ => Json("[]")
                };
            }

            if (path.StartsWith("/api/types/", StringComparison.Ordinal))
                return Json("{}");
            if (path == $"/api/people/{PersonHandle}")
                return Json($$"""{"handle":"{{PersonHandle}}","gramps_id":"I0001","primary_name":{"first_name":"John","surname_list":[{"surname":"Smith","primary":true}]},"event_ref_list":[]}""");
            if (path == "/api/places/" && query.StartsWith("?gramps_id=", StringComparison.Ordinal))
                return Json(query.Contains("P0003") ? """[{"handle":"stored-place-h","gramps_id":"P0003"}]""" : "[]");
            if (path == "/api/places/")
            {
                PlaceListPath = request.RequestUri.PathAndQuery;
                return PlaceListStatus == HttpStatusCode.OK
                    ? Json(PlaceList)
                    : new HttpResponseMessage(PlaceListStatus) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            if (path == "/api/places/stored-place-h")
                return Json("""{"handle":"stored-place-h","gramps_id":"P0003","name":{"value":"St. Petersburg"}}""");

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
