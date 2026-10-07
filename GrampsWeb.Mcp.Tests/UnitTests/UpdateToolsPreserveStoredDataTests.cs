using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// Gramps Web replaces the whole object on PUT, so every update tool must send back what it was not asked to change,
/// including fields the typed models do not carry (GitHub issue #5).
/// </summary>
public class UpdateToolsPreserveStoredDataTests
{
    [Fact]
    public async Task UpdatePerson_Sends_Back_Everything_It_Was_Not_Asked_To_Change()
    {
        const string stored = """
            {
              "_class": "Person", "handle": "h-person", "gramps_id": "I0100", "gender": 2,
              "primary_name": {
                "_class": "Name", "first_name": "Ivan", "citation_list": ["c-name"], "future_field": 1,
                "surname_list": [{ "_class": "Surname", "surname": "Petrov", "primary": true }]
              },
              "attribute_list": [{
                "_class": "Attribute", "type": { "_class": "AttributeType", "string": "Occupation" }, "value": "Smith",
                "citation_list": ["c-attr"], "note_list": ["n-attr"], "private": true
              }],
              "event_ref_list": [{ "_class": "EventRef", "ref": "e-birth", "role": "Primary", "private": true, "citation_list": ["c-ref"] }],
              "lds_ord_list": [{ "_class": "LdsOrd", "type": 1, "status": 0, "temple": "SLAKE", "citation_list": ["c-lds"] }],
              "media_list": [{ "_class": "MediaRef", "ref": "m-photo", "rect": [10.5, 20, 30.25, 40], "private": true }],
              "future_field": { "kept": true },
              "change": 1700000000,
              "profile": { "name_given": "Ivan" },
              "backlinks": { "family": [] }
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await PersonTools.UpdatePerson("h-person", gender: "male", client: CreateClient(handler));

        var expected = Parse(stored);
        expected.Remove("profile");
        expected.Remove("backlinks");
        expected["gender"] = JsonNode.Parse("1");
        AssertJsonEqual(expected, handler.Body);
        Assert.Equal("/api/people/h-person", handler.PutPath);
    }

    [Fact]
    public async Task UpdatePerson_Replace_Keeps_Metadata_Of_Links_It_Keeps_And_Matches_Duplicates_In_Order()
    {
        const string stored = """
            {
              "handle": "h-person", "gramps_id": "I0100",
              "event_ref_list": [
                { "_class": "EventRef", "ref": "e1", "role": "Primary", "private": true, "citation_list": ["c1"] },
                { "_class": "EventRef", "ref": "e1", "role": "Witness", "note_list": ["n1"] },
                { "_class": "EventRef", "ref": "e2", "role": "Primary" }
              ]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await PersonTools.UpdatePerson("h-person",
            eventRefs: Deserialize<FlexibleEventRefList>("""["e1::Primary", "e1::Witness", "e3"]"""),
            client: CreateClient(handler));

        var refs = handler.Body!["event_ref_list"]!.AsArray();
        Assert.Equal(3, refs.Count);
        AssertJsonEqual(Parse("""{ "_class": "EventRef", "ref": "e1", "role": "Primary", "private": true, "citation_list": ["c1"] }"""), refs[0]);
        AssertJsonEqual(Parse("""{ "_class": "EventRef", "ref": "e1", "role": "Witness", "note_list": ["n1"] }"""), refs[1]);
        Assert.Equal("e3", refs[2]!["ref"]!.GetValue<string>());
        Assert.Equal("Primary", refs[2]!["role"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("add", new[] { "m-photo", "m-scan", "m-new" })]
    [InlineData("remove", new[] { "m-photo" })]
    public async Task UpdatePerson_Add_And_Remove_Leave_Other_Stored_Links_Untouched(string mode, string[] expectedRefs)
    {
        const string stored = """
            {
              "handle": "h-person", "gramps_id": "I0100",
              "media_list": [
                { "_class": "MediaRef", "ref": "m-photo", "rect": [10.5, 20, 30.25, 40], "private": true, "citation_list": ["c1"] },
                { "_class": "MediaRef", "ref": "m-scan" }
              ]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await PersonTools.UpdatePerson("h-person",
            mediaHandles: new FlexibleHandleList { Handles = mode == "add" ? ["m-new"] : ["m-scan"] },
            client: CreateClient(handler), linkMode: mode);

        var media = handler.Body!["media_list"]!.AsArray();
        Assert.Equal(expectedRefs, media.Select(m => m!["ref"]!.GetValue<string>()));
        AssertJsonEqual(Parse(stored)["media_list"]![0], media[0]);
    }

    [Fact]
    public async Task UpdatePerson_Attributes_Keep_Citations_Notes_And_Privacy_Of_Unchanged_Entries()
    {
        const string stored = """
            {
              "handle": "h-person", "gramps_id": "I0100",
              "attribute_list": [
                { "_class": "Attribute", "type": { "_class": "AttributeType", "string": "Occupation" }, "value": "Smith",
                  "citation_list": ["c1"], "note_list": ["n1"], "private": true },
                { "_class": "Attribute", "type": "Nickname", "value": "Vanya", "citation_list": ["c2"] }
              ]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await PersonTools.UpdatePerson("h-person",
            attributes: Deserialize<FlexibleAttributeList>("""[{ "type": "Occupation", "value": "Smith" }, { "type": "Caste", "value": "Peasant" }]"""),
            client: CreateClient(handler));

        var attributes = handler.Body!["attribute_list"]!.AsArray();
        Assert.Equal(2, attributes.Count);
        AssertJsonEqual(Parse(stored)["attribute_list"]![0], attributes[0]);
        AssertJsonEqual(Parse("""{ "type": "Caste", "value": "Peasant" }"""), attributes[1]);
    }

    [Fact]
    public async Task UpdateFamily_Keeps_Child_Ref_Citations_Notes_And_Privacy()
    {
        const string stored = """
            {
              "_class": "Family", "handle": "h-family", "gramps_id": "F0100", "father_handle": "i-father", "type": "Married",
              "child_ref_list": [{
                "_class": "ChildRef", "ref": "i-child", "frel": "Birth", "mrel": "Birth",
                "citation_list": ["c1"], "note_list": ["n1"], "private": true
              }],
              "event_ref_list": [{ "_class": "EventRef", "ref": "e-marriage", "role": "Family", "private": true }],
              "lds_ord_list": [{ "_class": "LdsOrd", "type": 4, "status": 0 }]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await FamilyTools.UpdateFamily("h-family",
            childRefs: Deserialize<FlexibleChildRefList>("""["i-child", "i-second"]"""),
            client: CreateClient(handler));

        var expected = Parse(stored);
        expected["child_ref_list"]!.AsArray().Add(Parse("""{ "ref": "i-second", "frel": "Birth", "mrel": "Birth" }"""));
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateNote_Same_Text_Keeps_Styling_And_Links()
    {
        var handler = new StoredObjectHandler(StoredNote);

        await NoteTools.UpdateNote("h-note", text: "See the 1858 census", tagHandles: new FlexibleHandleList { Handles = ["t2"] },
            client: CreateClient(handler), linkMode: "add");

        var expected = Parse(StoredNote);
        expected["tag_list"]!.AsArray().Add("t2");
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateNote_New_Text_Replaces_The_Styled_Text_And_Keeps_The_Rest()
    {
        var handler = new StoredObjectHandler(StoredNote);

        await NoteTools.UpdateNote("h-note", text: "See the 1858 revision list", client: CreateClient(handler));

        var text = handler.Body!["text"]!;
        Assert.Equal("See the 1858 revision list", text["string"]!.GetValue<string>());
        Assert.Empty(text["tags"]!.AsArray());
        var expected = Parse(StoredNote);
        expected["text"] = text.DeepClone();
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateEvent_Empty_Date_Removes_Only_The_Date()
    {
        const string stored = """
            {
              "_class": "Event", "handle": "h-event", "gramps_id": "E0100", "type": "Birth", "place": "p1", "description": "d",
              "date": { "_class": "Date", "modifier": 0, "quality": 0, "dateval": [1, 2, 1858, false], "sortval": 2399000 },
              "attribute_list": [{ "_class": "Attribute", "type": "Age", "value": "30", "citation_list": ["c1"], "private": true }],
              "citation_list": ["c2"], "media_list": [{ "_class": "MediaRef", "ref": "m1", "rect": [1, 2, 3, 4] }]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await EventTools.UpdateEvent("h-event", date: "", client: CreateClient(handler));

        var expected = Parse(stored);
        expected.Remove("date");
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateCitation_Without_Confidence_Keeps_The_Stored_Confidence()
    {
        const string stored = """
            {
              "_class": "Citation", "handle": "h-citation", "gramps_id": "C0100", "source_handle": "s1", "page": "f. 1, l. 2",
              "confidence": 4, "date": { "_class": "Date", "modifier": 0, "dateval": [0, 0, 1858, false], "sortval": 2399000 },
              "attribute_list": [{ "_class": "SrcAttribute", "type": "Page", "value": "2", "private": true }],
              "note_list": ["n1"]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await CitationTools.UpdateCitation("h-citation", page: new FlexibleString { Value = "f. 1, l. 3" }, client: CreateClient(handler));

        var expected = Parse(stored);
        expected["page"] = "f. 1, l. 3";
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateMedia_Keeps_Path_Mime_And_Checksum()
    {
        const string stored = """
            {
              "_class": "Media", "handle": "h-media", "gramps_id": "O0100", "path": "scans/page-1.jpg", "mime": "image/jpeg",
              "checksum": "0123456789abcdef", "desc": "old", "attribute_list": [{ "type": "Page", "value": "1", "note_list": ["n1"] }]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await MediaTools.UpdateMedia("h-media", description: "Page 1", client: CreateClient(handler));

        var expected = Parse(stored);
        expected["desc"] = "Page 1";
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateSource_Keeps_Repository_Ref_Metadata_For_Kept_Repositories()
    {
        const string stored = """
            {
              "_class": "Source", "handle": "h-source", "gramps_id": "S0100", "title": "Metric book",
              "reporef_list": [{ "_class": "RepoRef", "ref": "r1", "call_number": "f. 1, inv. 1, file 1", "media_type": "Book",
                                 "private": true, "note_list": ["n1"] }],
              "attribute_list": [{ "_class": "SrcAttribute", "type": "Language", "value": "ru", "private": true }]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await SourceTools.UpdateSource("h-source",
            repositoryHandles: Deserialize<FlexibleRepositoryRefList>("""["r1", "r2 : file 5"]"""),
            client: CreateClient(handler));

        var expected = Parse(stored);
        expected["reporef_list"]!.AsArray().Add(Parse("""{ "ref": "r2", "call_number": "file 5" }"""));
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdatePlace_Keeps_Urls_Title_Alt_Loc_And_Enclosing_Dates()
    {
        const string stored = """
            {
              "_class": "Place", "handle": "h-place", "gramps_id": "P0100", "title": "Old title", "place_type": "Village",
              "name": { "_class": "PlaceName", "value": "Springfield", "lang": "en",
                        "date": { "_class": "Date", "modifier": 5, "dateval": [0, 0, 1795, false, 0, 0, 1917, false], "sortval": 2376672 } },
              "placeref_list": [{ "_class": "PlaceRef", "ref": "p-parent",
                                  "date": { "_class": "Date", "modifier": 3, "dateval": [0, 0, 1923, false], "sortval": 2423421 } }],
              "urls": [{ "_class": "Url", "path": "https://example.org/springfield", "desc": "", "type": "Web Home" }],
              "alt_loc": [{ "_class": "Location", "street": "", "city": "Springfield", "county": "Old county" }],
              "alt_names": [{ "_class": "PlaceName", "value": "Old Springfield", "lang": "en" }]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await PlaceTools.UpdatePlace("h-place", name: "Springfield Town",
            enclosedBy: Deserialize<FlexiblePlaceRefList>("""["p-parent"]"""), client: CreateClient(handler));

        var expected = Parse(stored);
        expected["name"]!["value"] = "Springfield Town";
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task UpdateRepository_Notes_Change_Keeps_Address_And_Urls()
    {
        const string stored = """
            {
              "_class": "Repository", "handle": "h-repo", "gramps_id": "R0100", "name": "City Archive", "type": "Archive",
              "address_list": [{ "_class": "Address", "street": "Main St 1", "city": "Springfield", "private": true,
                                 "citation_list": ["c1"] }],
              "urls": [{ "_class": "Url", "path": "mailto:archive@example.org", "desc": "", "type": "E-mail" }],
              "note_list": ["n1"]
            }
            """;
        var handler = new StoredObjectHandler(stored);

        await RepositoryTools.UpdateRepository("h-repo", noteHandles: new FlexibleHandleList { Handles = ["n2"] },
            client: CreateClient(handler), linkMode: "add");

        var expected = Parse(stored);
        expected["note_list"]!.AsArray().Add("n2");
        AssertJsonEqual(expected, handler.Body);
    }

    [Fact]
    public async Task Update_Refuses_A_Stored_Object_That_Is_Not_An_Object()
    {
        var handler = new StoredObjectHandler("[]");

        await Assert.ThrowsAsync<McpException>(() => EventTools.UpdateEvent("h-event", description: "d", client: CreateClient(handler)));

        Assert.Null(handler.Body);
    }

    private const string StoredNote = """
        {
          "_class": "Note", "handle": "h-note", "gramps_id": "N0100", "type": "General", "format": 0, "tag_list": ["t1"],
          "text": {
            "_class": "StyledText", "string": "See the 1858 census",
            "tags": [{ "_class": "StyledTextTag", "name": { "_class": "StyledTextTagType", "string": "Link" },
                       "value": "gramps://Person/handle/h-person", "ranges": [[4, 19]] },
                     { "_class": "StyledTextTag", "name": { "_class": "StyledTextTagType", "string": "Bold" },
                       "value": null, "ranges": [[0, 3]] }]
          }
        }
        """;

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected:\n{expected}\nActual:\n{actual}");

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://update-preserve.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Answers the token route and every GET with one stored object, and records the PUT body.</summary>
    private sealed class StoredObjectHandler(string stored) : HttpMessageHandler
    {
        public JsonNode? Body { get; private set; }

        public string? PutPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json;
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                json = """{"access_token":"token","refresh_token":"refresh","expires_in":900}""";
            else if (request.Method == HttpMethod.Get)
                json = stored;
            else
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                PutPath = request.RequestUri.AbsolutePath;
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                json = "{}";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
