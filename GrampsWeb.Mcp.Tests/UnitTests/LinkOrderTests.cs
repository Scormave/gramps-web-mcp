using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>Children, Families, Citations and Media in the <c>sort</c> argument of update_person and update_family.</summary>
public class LinkOrderTests
{
    [Fact]
    public async Task UpdateFamily_Sort_Children_Orders_By_Birth_Else_Baptism_And_Keeps_Their_Metadata()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                {
                  "handle": "h-family", "gramps_id": "F0100",
                  "child_ref_list": [
                    { "_class": "ChildRef", "ref": "c-godchild", "frel": "Birth", "mrel": "Birth" },
                    { "_class": "ChildRef", "ref": "c-late", "frel": "Adopted", "mrel": "Birth", "private": true },
                    { "_class": "ChildRef", "ref": "c-baptized", "frel": "Birth", "mrel": "Birth" },
                    { "_class": "ChildRef", "ref": "c-early", "frel": "Birth", "mrel": "Birth", "note_list": ["n1"] },
                    { "_class": "ChildRef", "ref": "c-missing", "frel": "Birth", "mrel": "Birth" }
                  ]
                }
                """,
            ["/api/people/?handles=c-godchild,c-late,c-baptized,c-early,c-missing&extend=event_ref_list&page=1&pagesize=5"] = """
                [
                  {
                    "handle": "c-godchild", "birth_ref_index": -1,
                    "event_ref_list": [{ "ref": "e-other-baptism", "role": "Witness" }],
                    "extended": { "events": [{ "handle": "e-other-baptism", "type": "Baptism", "date": { "dateval": [0, 0, 1850, false] } }] }
                  },
                  {
                    "handle": "c-late", "birth_ref_index": 0,
                    "event_ref_list": [{ "ref": "e-late-birth", "role": "Primary" }],
                    "extended": { "events": [{ "handle": "e-late-birth", "type": "Birth", "date": { "dateval": [0, 0, 1870, false] } }] }
                  },
                  {
                    "handle": "c-baptized", "birth_ref_index": 0,
                    "event_ref_list": [{ "ref": "e-undated-birth", "role": "Primary" }, { "ref": "e-baptism", "role": "Primary" }],
                    "extended": { "events": [
                      { "handle": "e-undated-birth", "type": "Birth", "date": { "dateval": [0, 0, 0, false] } },
                      { "handle": "e-baptism", "type": "Baptism", "date": { "dateval": [0, 0, 1860, false] } }
                    ] }
                  },
                  {
                    "handle": "c-early", "birth_ref_index": -1,
                    "event_ref_list": [{ "ref": "e-early-birth", "role": "" }],
                    "extended": { "events": [{ "handle": "e-early-birth", "type": "Birth", "date": { "dateval": [0, 0, 1858, false] } }] }
                  }
                ]
                """
        });
        var stored = JsonNode.Parse(handler.Bodies["/api/families/h-family"])!["child_ref_list"]!;

        await FamilyTools.UpdateFamily("h-family", client: CreateClient(handler), sort: SortOf("\"Children\""));

        var refs = handler.Body!["child_ref_list"]!.AsArray();
        Assert.Equal(["c-early", "c-baptized", "c-late", "c-godchild", "c-missing"], RefsOf(refs));
        AssertJsonEqual(stored[3], refs[0]);
        AssertJsonEqual(stored[1], refs[2]);
        Assert.Equal("/api/families/h-family", handler.PutPath);
    }

    [Fact]
    public async Task UpdatePerson_Sort_Families_Orders_By_Marriage_Else_Banns_Else_The_First_Child()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """
                {
                  "handle": "h-person", "gramps_id": "I0100",
                  "family_list": ["f-undated", "f-second", "f-banns", "f-children", "f-first"]
                }
                """,
            ["/api/families/?handles=f-undated,f-second,f-banns,f-children,f-first&extend=event_ref_list&page=1&pagesize=5"] = """
                [
                  { "handle": "f-undated", "child_ref_list": [{ "ref": "k-undated" }] },
                  {
                    "handle": "f-second", "event_ref_list": [{ "ref": "e-second", "role": "Family" }],
                    "extended": { "events": [{ "handle": "e-second", "type": "Marriage", "date": { "dateval": [0, 0, 1880, false] } }] }
                  },
                  {
                    "handle": "f-banns",
                    "event_ref_list": [{ "ref": "e-undated", "role": "Family" }, { "ref": "e-banns", "role": "Family" }],
                    "extended": { "events": [
                      { "handle": "e-undated", "type": "Marriage", "date": { "dateval": [0, 0, 0, false] } },
                      { "handle": "e-banns", "type": "Marriage Banns", "date": { "dateval": [0, 0, 1870, false] } }
                    ] }
                  },
                  { "handle": "f-children", "child_ref_list": [{ "ref": "k-1867" }, { "ref": "k-1865" }] },
                  {
                    "handle": "f-first", "event_ref_list": [{ "ref": "e-first", "role": "Family" }],
                    "extended": { "events": [{ "handle": "e-first", "type": "Marriage", "date": { "dateval": [0, 0, 1860, false] } }] }
                  }
                ]
                """,
            ["/api/people/?handles=k-undated,k-1867,k-1865&extend=event_ref_list&page=1&pagesize=3"] = """
                [
                  { "handle": "k-undated" },
                  {
                    "handle": "k-1867", "birth_ref_index": 0, "event_ref_list": [{ "ref": "b-1867", "role": "Primary" }],
                    "extended": { "events": [{ "handle": "b-1867", "type": "Birth", "date": { "dateval": [0, 0, 1867, false] } }] }
                  },
                  {
                    "handle": "k-1865", "birth_ref_index": 0, "event_ref_list": [{ "ref": "b-1865", "role": "Primary" }],
                    "extended": { "events": [{ "handle": "b-1865", "type": "Birth", "date": { "dateval": [0, 0, 1865, false] } }] }
                  }
                ]
                """
        });

        await PersonTools.UpdatePerson("h-person", client: CreateClient(handler), sort: SortOf("[\"families\"]"));

        Assert.Equal(
            ["f-first", "f-children", "f-banns", "f-second", "f-undated"],
            handler.Body!["family_list"]!.AsArray().Select(f => f!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdatePerson_Sort_Citations_And_Media_By_Their_Date_Keeps_Ties_In_Order()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """
                {
                  "handle": "h-person", "gramps_id": "I0100",
                  "event_ref_list": [{ "ref": "e-death", "role": "Primary" }, { "ref": "e-birth", "role": "Primary" }],
                  "citation_list": ["c-undated", "c-1900", "c-1850", "c-1900b"],
                  "media_list": [
                    { "_class": "MediaRef", "ref": "m-undated", "rect": [0, 0, 100, 100] },
                    { "_class": "MediaRef", "ref": "m-1900", "rect": [10, 20, 30, 40], "note_list": ["n1"] },
                    { "_class": "MediaRef", "ref": "m-1850", "private": true }
                  ]
                }
                """,
            ["/api/citations/?handles=c-undated,c-1900,c-1850,c-1900b&page=1&pagesize=4"] = """
                [
                  { "handle": "c-undated", "date": { "dateval": [0, 0, 0, false] } },
                  { "handle": "c-1900", "date": { "dateval": [0, 0, 1900, false] } },
                  { "handle": "c-1850", "date": { "dateval": [0, 0, 1850, false] } },
                  { "handle": "c-1900b", "date": { "dateval": [0, 0, 1900, false] } }
                ]
                """,
            ["/api/media/?handles=m-undated,m-1900,m-1850&page=1&pagesize=3"] = """
                [
                  { "handle": "m-undated" },
                  { "handle": "m-1900", "date": { "dateval": [0, 0, 1900, false] } },
                  { "handle": "m-1850", "date": { "dateval": [0, 0, 1850, false] } }
                ]
                """
        });
        var stored = JsonNode.Parse(handler.Bodies["/api/people/h-person"])!["media_list"]!;

        await PersonTools.UpdatePerson("h-person", client: CreateClient(handler), sort: SortOf("\"Citations | Media\""));

        Assert.Equal(
            ["c-1850", "c-1900", "c-1900b", "c-undated"],
            handler.Body!["citation_list"]!.AsArray().Select(c => c!.GetValue<string>()));
        var media = handler.Body!["media_list"]!.AsArray();
        Assert.Equal(["m-1850", "m-1900", "m-undated"], RefsOf(media));
        AssertJsonEqual(stored[2], media[0]);
        AssertJsonEqual(stored[1], media[1]);
        Assert.Equal(["e-death", "e-birth"], RefsOf(handler.Body!["event_ref_list"]!.AsArray()));
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/events/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateFamily_Sort_Does_Not_Read_A_List_Of_One()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                { "handle": "h-family", "gramps_id": "F0100", "child_ref_list": [{ "ref": "c-only" }], "citation_list": ["c1"] }
                """
        });

        await FamilyTools.UpdateFamily("h-family", client: CreateClient(handler), sort: SortOf("\"Children|Citations|Media\""));

        Assert.Equal(["/api/families/h-family", "/api/families/h-family"], handler.Requests);
    }

    [Theory]
    [InlineData("\"events | CHILDREN\"", SortedLists.Events | SortedLists.Children)]
    [InlineData("[\"media\", \" Citations \"]", SortedLists.Media | SortedLists.Citations)]
    [InlineData("\"Events, Media;\\nEvents\"", SortedLists.Events | SortedLists.Media)]
    [InlineData("\"\"", SortedLists.None)]
    [InlineData("null", SortedLists.None)]
    public void Parse_Reads_A_String_Or_An_Array_Of_Names_In_Any_Case(string json, SortedLists expected) =>
        Assert.Equal(expected, LinkOrder.Parse(SortOf(json), LinkOrder.FamilyLists));

    [Theory]
    [InlineData("\"Children\"", "Children")]
    [InlineData("\"Events | Marriages\"", "Marriages")]
    [InlineData("[1]", "1")]
    [InlineData("true", "true")]
    public void Parse_Names_A_List_The_Tool_Does_Not_Sort(string json, string rejected)
    {
        var ex = Assert.Throws<McpException>(() => LinkOrder.Parse(SortOf(json), LinkOrder.PersonLists));

        Assert.Equal(
            $"sort: \"{rejected}\" is not a list this tool sorts; use Events, Families, Citations, Media.",
            ex.Message);
    }

    [Fact]
    public async Task UpdatePerson_With_An_Unknown_List_Saves_Nothing()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """{ "handle": "h-person", "gramps_id": "I0100" }"""
        });

        await Assert.ThrowsAsync<McpException>(() =>
            PersonTools.UpdatePerson("h-person", gender: "male", client: CreateClient(handler), sort: SortOf("\"Children\"")));

        Assert.Null(handler.Body);
    }

    [Theory]
    [InlineData(nameof(PersonTools.UpdatePerson), typeof(PersonTools))]
    [InlineData(nameof(FamilyTools.UpdateFamily), typeof(FamilyTools))]
    public void An_Old_SortEvents_Argument_Is_Pointed_To_Sort(string method, Type tools)
    {
        var tool = McpServerTool.Create(tools.GetMethod(method)!);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"handle":"I0100","sortEvents":true}""");

        Assert.Contains("sortEvents (did you mean sort?)", ToolArgumentValidator.CheckNames(tool.ProtocolTool.InputSchema, arguments));
    }

    private static FlexibleSortList? SortOf(string json) => JsonSerializer.Deserialize<FlexibleSortList>(json);

    private static IEnumerable<string> RefsOf(JsonArray refs) => refs.Select(r => r!["ref"]!.GetValue<string>());

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected:\n{expected}\nActual:\n{actual}");

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://link-order.test", "user", "pass", "tree");
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
