using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// The attributes of event and media links (the <c>attribute_list</c> of an <c>EventRef</c> or <c>MediaRef</c>), such
/// as the age a register gives for one participant of an event: read from tool arguments, checked, sent and shown.
/// </summary>
public class LinkAttributesTests
{
    private const string StoredAge = """{"_class":"Attribute","type":"Age","value":"65","citation_list":["c1"],"note_list":[],"private":false}""";

    [Theory]
    [InlineData("""[{"ref":"e1","role":"Primary","attributes":["Age: 65"]}]""")]
    [InlineData("""[{"ref":"e1","attributes":"Age: 65"}]""")]
    [InlineData("""[{"ref":"e1","attribute_list":[{"type":"Age","value":"65"}]}]""")]
    [InlineData("""[{"ref":"e1","attributeList":[{"type":"Age","value":"65"}]}]""")]
    public void EventRefs_Read_Link_Attributes_In_The_Forms_Of_The_Attributes_Argument(string json)
    {
        var link = Assert.Single(Deserialize<FlexibleEventRefList>(json).Items);

        var attribute = Assert.Single(link.AttributeList!);
        Assert.Equal("Age", attribute.Type);
        Assert.Equal("65", attribute.Value);
    }

    [Fact]
    public void EventRefs_Tell_An_Empty_Attribute_List_From_None()
    {
        var items = Deserialize<FlexibleEventRefList>("""[{"ref":"e1","attributes":[]},{"ref":"e2","role":"Witness"},"e3::Witness"]""").Items;

        Assert.Empty(items[0].AttributeList!);
        Assert.Null(items[1].AttributeList);
        Assert.Null(items[2].AttributeList);
    }

    [Fact]
    public void MediaLinks_Read_And_Write_Link_Attributes()
    {
        const string json = """
            [{"ref":"m2","attributes":[{"type":"Page","value":"12"}]},
             {"ref":"m3","region":[1,2,3,4],"attributes":"Description: entry 14"},
             "m4"]
            """;

        var links = Deserialize<FlexibleMediaLinkList>(json);

        Assert.Equal(("Page", "12"), (links.Items[0].Attributes![0].Type, links.Items[0].Attributes![0].Value));
        Assert.Null(links.Items[0].Region);
        Assert.NotNull(links.Items[1].Region);
        Assert.Equal("Description", Assert.Single(links.Items[1].Attributes!).Type);
        Assert.Null(links.Items[2].Attributes);
        Assert.Equal(
            """[{"ref":"m2","attributes":[{"type":"Page","value":"12"}]},""" +
            """{"ref":"m3","region":[1,2,3,4],"attributes":[{"type":"Description","value":"entry 14"}]},"m4"]""",
            JsonSerializer.Serialize(links));
    }

    [Fact]
    public async Task CreatePerson_Sends_Event_Link_Attributes_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler();

        await PersonTools.CreatePerson(Name(),
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"h-event","role":"primary","attributes":["age: 65"]}]"""),
            client: CreateClient(handler));

        var link = handler.Body!["event_ref_list"]![0]!;
        Assert.Equal("Primary", link["role"]!.GetValue<string>());
        AssertJsonEqual(JsonNode.Parse("""[{"type":"Age","value":"65"}]"""), link["attribute_list"]);
    }

    [Fact]
    public async Task CreatePerson_Rejects_A_Misspelt_Link_Attribute_Before_Saving()
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => PersonTools.CreatePerson(Name(),
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"h-event","attributes":["Agee: 65"]}]"""),
            client: CreateClient(handler)));

        Assert.StartsWith("eventRefs: Invalid attribute types 'Agee'.", error.Message);
        Assert.Contains("Age", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdatePerson_Replace_Keeps_The_Citations_Of_A_Link_Attribute_It_Repeats()
    {
        var handler = new TreeHandler(PersonWithEvent(StoredAge));

        await PersonTools.UpdatePerson("h-person",
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"e1","role":"Primary","attributes":["Age: 65","Occupation: Farmer"]}]"""),
            client: CreateClient(handler));

        AssertJsonEqual(
            JsonNode.Parse($$"""[{{StoredAge}},{"type":"Occupation","value":"Farmer"}]"""),
            handler.Body!["event_ref_list"]![0]!["attribute_list"]);
    }

    [Theory]
    [InlineData("""[{"ref":"e1","role":"Primary"}]""", $"[{StoredAge}]")]
    [InlineData("""["e1"]""", $"[{StoredAge}]")]
    [InlineData("""[{"ref":"e1","attributes":[]}]""", "[]")]
    public async Task UpdatePerson_Replace_Keeps_Link_Attributes_Unless_Given_And_An_Empty_List_Removes_Them(
        string eventRefs, string expected)
    {
        var handler = new TreeHandler(PersonWithEvent(StoredAge));

        await PersonTools.UpdatePerson("h-person", eventRefs: Deserialize<FlexibleEventRefList>(eventRefs),
            client: CreateClient(handler));

        var link = handler.Body!["event_ref_list"]![0]!;
        Assert.Equal("Primary", link["role"]!.GetValue<string>());
        AssertJsonEqual(JsonNode.Parse(expected), link["attribute_list"]);
    }

    [Fact]
    public async Task UpdatePerson_Add_Gives_A_Linked_Event_The_Attributes_It_Lacks()
    {
        var handler = new TreeHandler(PersonWithEvent(StoredAge));

        await PersonTools.UpdatePerson("h-person",
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"e1","attributes":["Age: 65","Occupation: Farmer"]}]"""),
            client: CreateClient(handler), linkMode: "add");

        var links = handler.Body!["event_ref_list"]!.AsArray();
        Assert.Single(links);
        AssertJsonEqual(
            JsonNode.Parse($$"""[{{StoredAge}},{"type":"Occupation","value":"Farmer"}]"""),
            links[0]!["attribute_list"]);
    }

    [Fact]
    public async Task UpdatePerson_Add_Puts_Link_Attributes_On_The_Link_With_The_Same_Role()
    {
        var handler = new TreeHandler("""
            {"_class":"Person","handle":"h-person","gramps_id":"I0001",
             "event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary"},{"_class":"EventRef","ref":"e1","role":"Informant"}]}
            """);

        await PersonTools.UpdatePerson("h-person",
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"e1","role":"Informant","attributes":["Age: 40"]}]"""),
            client: CreateClient(handler), linkMode: "add");

        AssertJsonEqual(
            JsonNode.Parse("""
                [{"_class":"EventRef","ref":"e1","role":"Primary"},
                 {"_class":"EventRef","ref":"e1","role":"Informant","attribute_list":[{"type":"Age","value":"40"}]}]
                """),
            handler.Body!["event_ref_list"]);
    }

    [Fact]
    public async Task UpdateFamily_Add_Gives_The_Marriage_Link_An_Attribute()
    {
        var handler = new TreeHandler("""
            {"_class":"Family","handle":"h-family","gramps_id":"F0001",
             "event_ref_list":[{"_class":"EventRef","ref":"e-marriage","role":"Family"}]}
            """);

        await FamilyTools.UpdateFamily("h-family",
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"e-marriage","attributes":["father age: 30"]}]"""),
            client: CreateClient(handler), linkMode: "add");

        AssertJsonEqual(
            JsonNode.Parse("""
                [{"_class":"EventRef","ref":"e-marriage","role":"Family","attribute_list":[{"type":"Father Age","value":"30"}]}]
                """),
            handler.Body!["event_ref_list"]);
    }

    [Fact]
    public async Task UpdatePerson_MediaHandles_Add_Puts_Attributes_On_A_Shown_Media_And_A_New_One()
    {
        var handler = new TreeHandler("""
            {"_class":"Person","handle":"h-person","gramps_id":"I0001",
             "media_list":[{"_class":"MediaRef","ref":"m1","rect":[5,50,95,75]}]}
            """);

        await PersonTools.UpdatePerson("h-person",
            mediaHandles: Deserialize<FlexibleMediaLinkList>(
                """[{"ref":"m1","attributes":["description: entry 14"]},{"ref":"m2","attributes":["Description: entry 15"]}]"""),
            client: CreateClient(handler), linkMode: "add");

        AssertJsonEqual(
            JsonNode.Parse("""
                [{"_class":"MediaRef","ref":"m1","rect":[5,50,95,75],"attribute_list":[{"type":"Description","value":"entry 14"}]},
                 {"_class":"MediaRef","ref":"m2","attribute_list":[{"type":"Description","value":"entry 15"}]}]
                """),
            handler.Body!["media_list"]);
    }

    [Theory]
    [InlineData("""["m1"]""", """[{"_class":"Attribute","type":"Description","value":"entry 14","citation_list":["c1"]}]""")]
    [InlineData("""[{"ref":"m1","attributes":[]}]""", "[]")]
    [InlineData("""[{"ref":"m1","attributes":["Description: entry 14","Age: 30"]}]""",
        """[{"_class":"Attribute","type":"Description","value":"entry 14","citation_list":["c1"]},{"type":"Age","value":"30"}]""")]
    public async Task UpdatePerson_MediaHandles_Replace_Merges_Link_Attributes(string mediaHandles, string expected)
    {
        var handler = new TreeHandler("""
            {"_class":"Person","handle":"h-person","gramps_id":"I0001",
             "media_list":[{"_class":"MediaRef","ref":"m1","rect":[5,50,95,75],
                            "attribute_list":[{"_class":"Attribute","type":"Description","value":"entry 14","citation_list":["c1"]}]}]}
            """);

        await PersonTools.UpdatePerson("h-person", mediaHandles: Deserialize<FlexibleMediaLinkList>(mediaHandles),
            client: CreateClient(handler));

        var link = Assert.Single(handler.Body!["media_list"]!.AsArray())!;
        AssertJsonEqual(JsonNode.Parse("[5,50,95,75]"), link["rect"]);
        AssertJsonEqual(JsonNode.Parse(expected), link["attribute_list"]);
    }

    [Fact]
    public async Task UpdatePerson_Rejects_A_Misspelt_Media_Link_Attribute_Before_Saving()
    {
        var handler = new TreeHandler("""{"_class":"Person","handle":"h-person","gramps_id":"I0001"}""");

        var error = await Assert.ThrowsAsync<McpException>(() => PersonTools.UpdatePerson("h-person",
            mediaHandles: Deserialize<FlexibleMediaLinkList>("""[{"ref":"m1","attributes":["Descripton: entry 14"]}]"""),
            client: CreateClient(handler), linkMode: "add"));

        Assert.StartsWith("mediaHandles: Invalid attribute types 'Descripton'.", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task UpdatePerson_Remove_Does_Not_Check_Link_Attributes()
    {
        var handler = new TreeHandler("""
            {"_class":"Person","handle":"h-person","gramps_id":"I0001",
             "event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary"}],
             "media_list":[{"_class":"MediaRef","ref":"m1"}]}
            """);

        await PersonTools.UpdatePerson("h-person",
            eventRefs: Deserialize<FlexibleEventRefList>("""[{"ref":"e1","attributes":["Agee: 65"]}]"""),
            mediaHandles: Deserialize<FlexibleMediaLinkList>("""[{"ref":"m1","attributes":["Descripton: x"]}]"""),
            client: CreateClient(handler), linkMode: "remove");

        Assert.Empty(handler.Body!["event_ref_list"]!.AsArray());
        Assert.Empty(handler.Body!["media_list"]!.AsArray());
    }

    [Fact]
    public void FormatInline_Puts_Link_Attributes_On_One_Line()
    {
        GrampsAttribute[] attributes =
        [
            new() { Type = "Age", Value = "65", Private = true, CitationList = ["c1", " "], NoteList = ["n1"] },
            new() { Type = "Occupation", Value = "Farmer" },
            new() { Type = "Witness" },
        ];

        Assert.Equal("Age: 65 ⚠ private [citations: c1] [notes: n1]; Occupation: Farmer; Witness: —",
            AttributeListFormatter.FormatInline(attributes));
        Assert.Null(AttributeListFormatter.FormatInline([]));
        Assert.Null(AttributeListFormatter.FormatInline(null));
    }

    [Fact]
    public async Task PersonCard_Shows_The_Attributes_Of_Each_Event_Link()
    {
        var handler = new TreeHandler(bodies: new Dictionary<string, string>
        {
            ["/api/events/?handles=e1,e2&profile=self&page=1&pagesize=2"] = """
                [{"handle":"e1","type":"Death","date":{"modifier":0,"dateval":[0,0,1920,false]}},
                 {"handle":"e2","type":"Census","date":{"modifier":0,"dateval":[0,0,1897,false]}}]
                """,
        });
        var person = JsonSerializer.Deserialize<GrampsPerson>(
            """
            {"handle":"p1","gramps_id":"I0001",
             "primary_name":{"first_name":"Pyotr","surname_list":[{"surname":"Obraztsov"}]},
             "event_ref_list":[{"ref":"e1","role":"Primary","attribute_list":[{"type":"Age","value":"65","citation_list":["c1"]}]},
                               {"ref":"e2","role":"Primary"}]}
            """,
            GrampsJson.Options)!;

        var result = (await PersonFormatter.FormatPersonFull(person, CreateClient(handler))).Replace("\r\n", "\n");

        Assert.Contains("[handle: e1] role: Primary | link attributes: Age: 65 [citations: c1]\n", result);
        Assert.Contains("[handle: e2] role: Primary\n", result);
    }

    [Fact]
    public async Task ExtendedPerson_Shows_The_Attributes_Of_An_Event_Link_Under_The_Event()
    {
        var person = JsonSerializer.Deserialize<GrampsPersonExtended>("""
            {"handle":"p1","gramps_id":"I0001",
             "event_ref_list":[{"ref":"census","role":"Primary","attribute_list":[{"type":"Age","value":"36"}]}],
             "extended":{"events":[{"handle":"census","type":"Census","date":{"modifier":0,"dateval":[0,0,1897,false]}}]}}
            """, GrampsJson.Options)!;

        var result = (await PersonFormatter.FormatPersonExtended(person, CreateClient(new TreeHandler()))).Replace("\r\n", "\n");

        Assert.Contains("  • Census: 1897 [Primary] [handle: census]\n    Link attributes: Age: 36\n", result);
    }

    [Fact]
    public async Task EventCard_Shows_The_Link_Attributes_Of_Each_Linked_Person()
    {
        var evt = new GrampsEvent { Handle = "e1", GrampsId = "E0001", Type = "Death" };

        var result = (await EventFormatter.FormatEventFull(evt, CreateClient(new TreeHandler()),
            [("p1", "Obraztsov, Pyotr", "Primary", "Age: 65"), ("p2", null, "Informant", null)])).Replace("\r\n", "\n");

        Assert.Contains("  • Obraztsov, Pyotr [handle: p1] role: Primary | link attributes: Age: 65\n", result);
        Assert.Contains("  • [handle: p2] role: Informant\n", result);
    }

    [Fact]
    public void LinkedPeople_Join_The_Attributes_Of_Every_Link_Of_A_Person_To_The_Event()
    {
        var person = JsonSerializer.Deserialize<GrampsPerson>(
            """
            {"handle":"p1","event_ref_list":[
              {"ref":"e1","role":"Primary","attribute_list":[{"type":"Age","value":"65"}]},
              {"ref":"e2","role":"Primary","attribute_list":[{"type":"Age","value":"3"}]},
              {"ref":"e1","role":"Informant","attribute_list":[{"type":"Occupation","value":"Farmer","citation_list":["c1"]}]},
              {"ref":"e3","role":"Primary"}]}
            """,
            GrampsJson.Options)!;

        Assert.Equal("Age: 65; Occupation: Farmer [citations: c1]", EventTools.ResolveLinkAttributesForPersonEvent(person, "e1"));
        Assert.Null(EventTools.ResolveLinkAttributesForPersonEvent(person, "e3"));
        Assert.Null(EventTools.ResolveLinkAttributesForPersonEvent(null, "e1"));
    }

    [Fact]
    public void Media_Sections_Show_The_Attributes_Of_A_Link()
    {
        var links = JsonSerializer.Deserialize<GrampsMediaRef[]>(
            """[{"ref":"m1","rect":[10,20,60,45],"attribute_list":[{"type":"Description","value":"entry 14"}]},{"ref":"m2"}]""",
            GrampsJson.Options)!;
        var section = new StringBuilder();
        var gallery = new StringBuilder();

        MediaFormatter.AppendMediaLinkSection(section, "Media", links, new Dictionary<string, string> { ["m1"] = "Page 12" });
        MediaFormatter.AppendExtendedMediaSection(gallery,
            [new GrampsMedia { Handle = "m1", Description = "Page 12" }, new GrampsMedia { Handle = "m2", Description = "Page 13" }],
            links);

        Assert.Equal(
            "\nMedia (2):\n" +
            "  • Page 12 [handle: m1] region [10, 20, 60, 45] | link attributes: Description: entry 14\n" +
            "  • [handle: m2]\n",
            section.ToString().Replace("\r\n", "\n"));
        Assert.Equal(
            "\nGallery (media) (2):\n" +
            "  • Page 12 [handle: m1] region [10, 20, 60, 45] | link attributes: Description: entry 14\n" +
            "  • Page 13 [handle: m2]\n",
            gallery.ToString().Replace("\r\n", "\n"));
    }

    private static string PersonWithEvent(string attribute) => $$"""
        {"_class":"Person","handle":"h-person","gramps_id":"I0001",
         "event_ref_list":[{"_class":"EventRef","ref":"e1","role":"Primary","private":false,"note_list":[],
                            "attribute_list":[{{attribute}}]}]}
        """;

    private static FlexibleGrampsName Name() =>
        Deserialize<FlexibleGrampsName>("""{ "given": "Pyotr", "surname": "Obraztsov" }""");

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected:\n{expected}\nActual:\n{actual}");

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        // A distinct URL per client keeps the caches of one test out of the next.
        var config = new GrampsConfig($"https://{Guid.NewGuid():N}.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves the type vocabularies, <paramref name="bodies"/> by path and query, and <paramref name="stored"/> for any
    /// other GET (404 without it), and records the POST or PUT body.
    /// </summary>
    private sealed class TreeHandler(string? stored = null, IReadOnlyDictionary<string, string>? bodies = null) : HttpMessageHandler
    {
        private const string DefaultTypes = """
            {
              "attribute_types": ["Unknown", "Caste", "Description", "Identification Number", "National Origin",
                                  "Number of Children", "Social Security Number", "Nickname", "Cause", "Agency", "Age",
                                  "Father Age", "Mother Age", "Witness", "Time", "Occupation"],
              "event_role_types": ["Unknown", "Primary", "Clergy", "Celebrant", "Aide", "Bride", "Groom", "Witness",
                                   "Family", "Informant"]
            }
            """;

        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue($"{request.Method} {path}");
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(request.Method == HttpMethod.Put
                    ? "{}"
                    : """[{ "_class": "Person", "type": "add", "old": null, "new": { "_class": "Person", "handle": "h-new", "gramps_id": "I0001" } }]""");
            }

            if (path == "/api/types/default/")
                return Json(DefaultTypes);
            if (path == "/api/types/custom/")
                return Json("{}");
            if (bodies is not null && bodies.TryGetValue(path, out var body))
                return Json(body);
            return stored is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(stored);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
