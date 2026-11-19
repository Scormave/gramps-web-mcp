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
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// A child's link to a family and a person's names carry citations and notes (Gramps <c>ChildRef</c> and
/// <c>Name</c>): the tools accept them, keep them on update and show them.
/// </summary>
public class LinkCitationsTests
{
    [Fact]
    public void ChildRef_Reads_Both_Spellings_And_Writes_Empty_Lists_But_Not_Missing_Ones()
    {
        var children = JsonSerializer.Deserialize<FlexibleChildRefList>("""
            [{ "ref": "h1", "frel": "Adopted", "citationList": ["c1"], "noteList": ["n1"] },
             { "ref": "h2", "citation_list": [], "note_list": ["n2"] },
             "h3"]
            """)!.Items!;

        Assert.Equal(["c1"], children[0].CitationList!);
        Assert.Equal(["n1"], children[0].NoteList!);
        AssertJsonEqual("""{"ref":"h1","frel":"Adopted","citation_list":["c1"],"note_list":["n1"]}""",
            GrampsObjectPatch.ToNode(children[0]));
        AssertJsonEqual("""{"ref":"h2","citation_list":[],"note_list":["n2"]}""", GrampsObjectPatch.ToNode(children[1]));
        Assert.Null(GrampsObjectPatch.ToNode(children[2])!["citation_list"]);
    }

    [Fact]
    public async Task CreateRecords_Cites_A_Child_Link_With_Records_Of_The_Batch()
    {
        var handler = new RecordingHandler();

        await CreateRecordsCall.RunAsync("""
            { "sources": [{ "key": "book", "title": "Adoption register" }],
              "citations": [{ "key": "cit", "sourceHandle": "#book", "page": "No. 7" }],
              "notes": [{ "key": "why", "text": "Taken in after the parents died." }],
              "people": [{ "key": "kid", "primaryName": { "given": "Ivan", "surname": "Petrov" } }],
              "families": [{ "childRefs": [{ "ref": "#kid", "frel": "Adopted", "mrel": "Adopted",
                                            "citation_list": ["#cit"], "note_list": ["#why"] }] }] }
            """, CreateClient(handler));

        var child = CreateRecordsCall.Saved(handler.PostBody, "Family")["child_ref_list"]![0]!;
        Assert.Equal(CreateRecordsCall.Saved(handler.PostBody, "Person")["handle"]!.GetValue<string>(), child["ref"]!.GetValue<string>());
        Assert.Equal([CreateRecordsCall.Saved(handler.PostBody, "Citation")["handle"]!.GetValue<string>()],
            child["citation_list"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Equal([CreateRecordsCall.Saved(handler.PostBody, "Note")["handle"]!.GetValue<string>()],
            child["note_list"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    private const string StoredFamily = """
        { "_class": "Family", "handle": "h-family", "gramps_id": "F0300",
          "child_ref_list": [{ "_class": "ChildRef", "ref": "h-child-1", "frel": "Adopted", "mrel": "Birth",
                               "citation_list": ["h-cit-old"], "note_list": ["h-note-old"] },
                             { "_class": "ChildRef", "ref": "h-child-2", "frel": "Birth", "mrel": "Birth" }] }
        """;

    [Fact]
    public async Task UpdateFamily_Add_Cites_A_Stored_Child_Link_By_Gramps_Id_And_Keeps_The_Rest()
    {
        var handler = new TreeHandler(StoredFamily, new Dictionary<string, string>
        {
            ["/api/citations/?gramps_id=C0301&pagesize=1"] = """[{ "handle": "h-cit-new" }]"""
        });

        await FamilyTools.UpdateFamily("h-family",
            childRefs: JsonSerializer.Deserialize<FlexibleChildRefList>("""[{ "ref": "h-child-1", "citation_list": ["C0301"] }]"""),
            client: CreateClient(handler), linkMode: "add");

        AssertJsonEqual("""
            [{ "_class": "ChildRef", "ref": "h-child-1", "frel": "Adopted", "mrel": "Birth",
               "citation_list": ["h-cit-old", "h-cit-new"], "note_list": ["h-note-old"] },
             { "_class": "ChildRef", "ref": "h-child-2", "frel": "Birth", "mrel": "Birth" }]
            """, handler.Body!["child_ref_list"]);
    }

    [Theory]
    [InlineData("""{ "ref": "h-child-1", "frel": "Adopted" }""", """["h-cit-old"]""")]
    [InlineData("""{ "ref": "h-child-1", "frel": "Adopted", "citation_list": [] }""", "[]")]
    public async Task UpdateFamily_Replace_Keeps_Child_Link_Citations_Unless_The_Link_Sends_Its_Own(string child, string citations)
    {
        var handler = new TreeHandler(StoredFamily);

        await FamilyTools.UpdateFamily("h-family",
            childRefs: JsonSerializer.Deserialize<FlexibleChildRefList>($"""[{child}, "h-child-2"]"""),
            client: CreateClient(handler), linkMode: "replace");

        var stored = handler.Body!["child_ref_list"]![0]!;
        AssertJsonEqual(citations, stored["citation_list"]);
        AssertJsonEqual("""["h-note-old"]""", stored["note_list"]);
    }

    private const string StoredPerson = """
        { "_class": "Person", "handle": "h-person", "gramps_id": "I0300", "gender": 1,
          "primary_name": { "_class": "Name", "first_name": "Ivan", "surname_list": [{ "_class": "Surname", "surname": "Petrov" }],
                            "citation_list": ["h-cit-name"], "note_list": ["h-note-name"] },
          "alternate_names": [
            { "_class": "Name", "type": { "_class": "NameType", "string": "Also Known As" }, "first_name": "Ivan",
              "surname_list": [{ "_class": "Surname", "surname": "Smith" }], "citation_list": ["h-cit-aka"] },
            { "_class": "Name", "first_name": "John", "surname_list": [{ "_class": "Surname", "surname": "Brown" }],
              "citation_list": ["h-cit-1"] },
            { "_class": "Name", "first_name": "John", "surname_list": [{ "_class": "Surname", "surname": "Brown" }],
              "citation_list": ["h-cit-2"], "note_list": ["h-note-2"] }] }
        """;

    [Theory]
    [InlineData("""{ "given": "Ivan", "surname": "Smith" }""", """["h-cit-name"]""", """["h-note-name"]""")]
    [InlineData("""{ "given": "Ivan", "surname": "Smith", "citation_list": [] }""", "[]", """["h-note-name"]""")]
    [InlineData("""{ "given": "Ivan", "surname": "Smith", "citation_list": ["C0301"], "note_list": [] }""", """["h-cit-new"]""", "[]")]
    public async Task UpdatePerson_Primary_Name_Keeps_Its_Citations_And_Notes_Unless_It_Sends_Its_Own(
        string name, string citations, string notes)
    {
        var handler = new TreeHandler(StoredPerson, new Dictionary<string, string>
        {
            ["/api/citations/?gramps_id=C0301&pagesize=1"] = """[{ "handle": "h-cit-new" }]"""
        });

        await PersonTools.UpdatePerson("h-person", primaryName: JsonSerializer.Deserialize<FlexibleGrampsName>(name),
            client: CreateClient(handler));

        var primary = handler.Body!["primary_name"]!;
        Assert.Equal("Smith", primary["surname_list"]![0]!["surname"]!.GetValue<string>());
        AssertJsonEqual(citations, primary["citation_list"]);
        AssertJsonEqual(notes, primary["note_list"]);
    }

    [Fact]
    public async Task UpdatePerson_Alternate_Names_Keep_Citations_And_Notes_Of_The_Same_Name_In_Order()
    {
        var handler = new TreeHandler(StoredPerson);

        await PersonTools.UpdatePerson("h-person", alternateNames: JsonSerializer.Deserialize<FlexibleAlternateNameList>("""
            [{ "given": "John", "surname": "Brown", "nick": "Jack" },
             { "type": "Married Name", "given": "Ivan", "surname": "Smith" },
             { "given": "John", "surname": "Brown", "citation_list": [] },
             { "type": "Also Known As", "given": "Ivan", "surname": "Smith", "title": "Dr." }]
            """), client: CreateClient(handler));

        var names = handler.Body!["alternate_names"]!.AsArray();
        Assert.Equal(4, names.Count);
        // The first John Brown keeps the first stored one's citations; the second one sends its own and keeps the
        // stored notes of the second; the married name matches nothing; the alias keeps its citations.
        AssertJsonEqual("""["h-cit-1"]""", names[0]!["citation_list"]);
        Assert.Equal("Jack", names[0]!["nick"]!.GetValue<string>());
        Assert.Null(names[1]!["citation_list"]);
        AssertJsonEqual("[]", names[2]!["citation_list"]);
        AssertJsonEqual("""["h-note-2"]""", names[2]!["note_list"]);
        AssertJsonEqual("""["h-cit-aka"]""", names[3]!["citation_list"]);
    }

    [Fact]
    public void FormatNameDetailed_Shows_The_Citations_And_Notes_Of_A_Name()
    {
        var name = new GrampsName
        {
            FirstName = "Ivan",
            SurnameList = [new GrampsSurname { Surname = "Petrov" }],
            CitationList = ["h-cit", "h-cit-2"],
            NoteList = ["h-note"]
        };

        var result = GrampsValueFormatter.FormatNameDetailed(name,
            labels: new Dictionary<string, string> { ["h-cit"] = "C0001 Metric book 1877, f. 12" });

        Assert.Contains("    citations: C0001 Metric book 1877, f. 12 [handle: h-cit]; [handle: h-cit-2]", result);
        Assert.Contains("    notes:   [handle: h-note]", result);
        Assert.Equal("    (empty name)" + Environment.NewLine + "    notes:   [handle: h-note]",
            GrampsValueFormatter.FormatNameDetailed(new GrampsName { NoteList = ["h-note"] }));
    }

    [Fact]
    public async Task ReadFamily_Shows_The_Citations_And_Notes_Of_A_Child_Link()
    {
        const string family = """
            { "handle": "h-family", "gramps_id": "F0300",
              "child_ref_list": [{ "ref": "h-child-1", "frel": "Adopted", "mrel": "Birth",
                                   "citation_list": ["h-cit"], "note_list": ["h-note"] }] }
            """;

        var result = (await FamilyTools.ReadFamilyAsync("h-family", client: CreateClient(new TreeHandler(family))))
            .Replace("\r\n", "\n");

        Assert.Contains("  • [handle: h-child-1] | frel: Adopted, mrel: Birth\n    Citations on link: ", result);
        Assert.Contains("[handle: h-cit]\n    Notes on link: ", result);
        Assert.Contains("[handle: h-note]\n", result);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://link-citations.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    private static void AssertJsonEqual(string expected, JsonNode? actual)
    {
        var parsed = JsonNode.Parse(expected);
        Assert.True(JsonNode.DeepEquals(parsed, actual), $"Expected:\n{parsed}\nActual:\n{actual}");
    }

    /// <summary>
    /// Answers Gramps ID lookups from <paramref name="lookups"/> (404 otherwise), every other GET with the stored
    /// record, and records the PUT body.
    /// </summary>
    private sealed class TreeHandler(string stored, IReadOnlyDictionary<string, string>? lookups = null) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public JsonNode? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Put)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json("{}");
            }

            if (path.Contains("gramps_id=", StringComparison.Ordinal))
                return lookups?.TryGetValue(path, out var found) == true ? Json(found) : new HttpResponseMessage(HttpStatusCode.NotFound);

            return Json(stored);
        }
    }

    /// <summary>Answers create_records: the POST body is recorded and answered with an "add" change per object.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public JsonNode? PostBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");
            if (request.RequestUri.AbsolutePath.StartsWith("/api/types/"))
                return Json("{}");
            if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/people/")
                return Json("[]");
            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            PostBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Json(CreateRecordsCall.Changes(PostBody!).ToJsonString());
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
