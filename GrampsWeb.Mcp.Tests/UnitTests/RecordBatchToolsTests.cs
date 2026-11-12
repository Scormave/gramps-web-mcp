using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>create_records sends every record in one POST to /api/objects/, with each "#key" replaced by a handle.</summary>
public class RecordBatchToolsTests
{
    private const string Family = """
        {
          "repositories": [{ "key": "archive", "name": "State Archive" }],
          "sources": [{ "key": "book", "title": "Metric book 1877", "repositoryHandles": ["#archive : 123"] }],
          "citations": [{ "key": "cit", "sourceHandle": "#book", "page": "f. 12" }],
          "events": [
            { "key": "birth", "eventType": "Birth", "date": "1877", "citationHandles": ["#cit"], "description": "#1 in the book" },
            { "key": "marriage", "eventType": "Marriage", "date": "1900" }
          ],
          "people": [
            { "key": "petr", "primaryName": "Petr Ivanov", "gender": "Male", "eventRefs": ["#birth"] },
            { "key": "ivan", "primaryName": "Ivan Petrov", "gender": "Male", "personAssociations": ["#petr:: Godfather"] }
          ],
          "families": [{ "fatherHandle": "#ivan", "motherHandle": "I0007", "eventRefs": ["#marriage"], "childRefs": ["#petr"] }]
        }
        """;

    [Fact]
    public async Task CreateRecords_Saves_Every_Record_In_One_Post_With_Keys_As_Handles()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/?gramps_id=I0007&pagesize=1"] = """[{ "handle": "h-mother", "gramps_id": "I0007" }]"""
        });

        await CreateRecords(Family, handler);

        Assert.Equal(["/api/objects/"], handler.Posts);
        var body = handler.Body!.AsArray();
        Assert.Equal(
            ["Repository", "Source", "Citation", "Event", "Event", "Person", "Person", "Family"],
            body.Select(o => o!["_class"]!.GetValue<string>()));
        var handles = body.Select(o => o!["handle"]!.GetValue<string>()).ToList();
        Assert.Equal(handles.Count, handles.Distinct().Count());
        Assert.All(handles, h => Assert.Matches("^[0-9a-f]{16,}$", h));

        var (repository, source, citation, birth, marriage, petr, ivan, family) =
            (body[0]!, body[1]!, body[2]!, body[3]!, body[4]!, body[5]!, body[6]!, body[7]!);
        Assert.Equal(handles[0], source["reporef_list"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal(handles[1], citation["source_handle"]!.GetValue<string>());
        Assert.Equal(handles[2], birth["citation_list"]![0]!.GetValue<string>());
        Assert.Equal("#1 in the book", birth["description"]!.GetValue<string>());
        Assert.Equal(handles[3], petr["event_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal(handles[5], ivan["person_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal(handles[6], family["father_handle"]!.GetValue<string>());
        Assert.Equal("h-mother", family["mother_handle"]!.GetValue<string>());
        Assert.Equal(handles[4], family["event_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal(handles[5], family["child_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.DoesNotContain("\"#", handler.Body.ToJsonString().Replace("\"#1 in the book\"", ""));
        Assert.Equal("State Archive", repository["name"]!.GetValue<string>());
        Assert.Equal("Marriage", marriage["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Looks_Up_Gramps_Ids_In_Link_Fields_Among_The_Records_The_Field_Links()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/places/?gramps_id=P0101&pagesize=1"] = """[{ "handle": "h-place" }]""",
            ["/api/citations/?gramps_id=C0101&pagesize=1"] = """[{ "handle": "h-citation" }]""",
            ["/api/notes/?gramps_id=N0101&pagesize=1"] = """[{ "handle": "h-note" }]""",
            ["/api/notes/?gramps_id=X0101&pagesize=1"] = """[{ "handle": "h-note-x" }]""",
            ["/api/events/?gramps_id=E0101&pagesize=1"] = """[{ "handle": "h-death" }]""",
            ["/api/events/h-death"] = """{ "handle": "h-death", "type": "Death" }"""
        });

        await CreateRecords("""
            {
              "events": [{ "key": "birth", "eventType": "Birth", "placeHandle": "P0101", "citationHandles": ["C0101"] }],
              "people": [{ "primaryName": "Ivan Petrov",
                           "eventRefs": ["#birth", { "ref": "E0101", "note_list": ["N0101"] }],
                           "noteHandles": ["N0101", "X0101"],
                           "attributes": [{ "type": "Age", "value": "65", "citation_list": ["C0101"] }] }]
            }
            """, handler);

        var body = handler.Body!.AsArray();
        var (birth, person) = (body[0]!, body[1]!);
        Assert.Equal("h-place", birth["place"]!.GetValue<string>());
        Assert.Equal(["h-citation"], birth["citation_list"]!.AsArray().Select(c => c!.GetValue<string>()));
        var eventRefs = person["event_ref_list"]!.AsArray();
        Assert.Equal(birth["handle"]!.GetValue<string>(), eventRefs[0]!["ref"]!.GetValue<string>());
        Assert.Equal("h-death", eventRefs[1]!["ref"]!.GetValue<string>());
        Assert.Equal(["h-note"], eventRefs[1]!["note_list"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(0, person["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(1, person["death_ref_index"]!.GetValue<int>());
        Assert.Equal(["h-note", "h-note-x"], person["note_list"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["h-citation"],
            person["attribute_list"]![0]!["citation_list"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Single(handler.Requests, r => r == "/api/notes/?gramps_id=N0101&pagesize=1");
    }

    [Fact]
    public async Task CreateRecords_Names_Each_Field_With_A_Gramps_Id_The_Tree_Lacks_And_Saves_Nothing()
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords("""
            {
              "people": [{ "key": "ivan", "primaryName": "Ivan Petrov", "eventRefs": ["E0404"], "noteHandles": ["I0404"] }]
            }
            """, handler));

        Assert.Equal(
            "Error: people[0] (key ivan): eventRefs: there is no event E0404. noteHandles: there is no note I0404. " +
            "I is the prefix of people, and noteHandles links notes. Nothing was saved.",
            error.Message);
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Refuses_A_Gramps_Id_As_A_Tag_Without_Asking_Gramps_Web()
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords("""
            { "notes": [{ "text": "To do", "tagHandles": ["T0001"] }] }
            """, handler));

        Assert.Equal(
            "Error: notes[0]: tagHandles: T0001 is not a tag handle; tags have no Gramps ID, so give the tag's handle " +
            "from list_objects(objectType: \"tags\"). Nothing was saved.",
            error.Message);
        Assert.DoesNotContain(handler.Requests, r => r.Contains("gramps_id=", StringComparison.Ordinal));
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Repository_Takes_Addresses_With_Separate_Fields()
    {
        var handler = new TreeHandler();

        await CreateRecords("""
            {
              "repositories": [{ "name": "State Archive",
                                 "addresses": [{ "street": "12 High Street", "city": "London", "postal": "SW1A 1AA",
                                                 "country": "United Kingdom" }] }]
            }
            """, handler);

        var address = Assert.Single(handler.Body!.AsArray()[0]!["address_list"]!.AsArray())!;
        Assert.Equal("12 High Street", address["street"]!.GetValue<string>());
        Assert.Equal("London", address["city"]!.GetValue<string>());
        Assert.Equal("SW1A 1AA", address["postal"]!.GetValue<string>());
        Assert.Equal("United Kingdom", address["country"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Repository_Takes_Urls_With_Type_And_Description()
    {
        var handler = new TreeHandler();

        await CreateRecords("""
            {
              "repositories": [{ "name": "State Archive",
                                 "urls": "Web Home: https://archive.example.org — reading room\nE-mail: info@archive.example.org" }]
            }
            """, handler);

        var urls = handler.Body!.AsArray()[0]!["urls"]!.AsArray();
        Assert.Equal(["Web Home", "E-mail"], urls.Select(u => u!["type"]!.GetValue<string>()));
        Assert.Equal("reading room", urls[0]!["desc"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Points_Vital_Indexes_At_Events_Of_The_Call_And_Existing_Ones()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/events/?handles=e-baptism,e-death&page=1&pagesize=2"] = """
                [{ "handle": "e-baptism", "type": "Baptism" }, { "handle": "e-death", "type": "Death" }]
                """
        });

        await CreateRecords("""
            {
              "events": [{ "key": "birth", "eventType": "Birth" }, { "key": "burial", "eventType": "Burial" }],
              "people": [
                { "primaryName": "Ivan Petrov", "eventRefs": ["e-baptism", "#burial", "#birth", "e-death"] },
                { "primaryName": "Anna Petrova", "eventRefs": ["#birth::Witness"] }
              ]
            }
            """, handler);

        var people = handler.Body!.AsArray().Where(o => o!["_class"]!.GetValue<string>() == "Person").ToList();
        Assert.Equal(2, people[0]!["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(3, people[0]!["death_ref_index"]!.GetValue<int>());
        Assert.Equal(-1, people[1]!["birth_ref_index"]!.GetValue<int>());
        Assert.Equal(-1, people[1]!["death_ref_index"]!.GetValue<int>());
        Assert.Equal(["/api/events/?handles=e-baptism,e-death&page=1&pagesize=2"],
            handler.Requests.Where(p => p.StartsWith("/api/events/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task CreateRecords_Sends_Event_Link_Attributes_With_Their_Citations_Keyed_In_The_Call()
    {
        var handler = new TreeHandler();

        await CreateRecords("""
            {
              "sources": [{ "key": "book", "title": "Metric book 1897" }],
              "citations": [{ "key": "cit", "sourceHandle": "#book", "page": "f. 12" }],
              "events": [{ "key": "death", "eventType": "Death", "date": "1897" }],
              "people": [{ "primaryName": "Ivan Petrov",
                           "eventRefs": [{ "ref": "#death", "attributes": [{ "type": "Age", "value": "65", "citation_list": ["#cit"] }] }] }]
            }
            """, handler);

        var body = handler.Body!.AsArray();
        var (citation, death, person) = (body[1]!, body[2]!, body[3]!);
        var link = person["event_ref_list"]![0]!;
        Assert.Equal(death["handle"]!.GetValue<string>(), link["ref"]!.GetValue<string>());
        var attribute = Assert.Single(link["attribute_list"]!.AsArray())!;
        Assert.Equal("Age", attribute["type"]!.GetValue<string>());
        Assert.Equal("65", attribute["value"]!.GetValue<string>());
        Assert.Equal(citation["handle"]!.GetValue<string>(), attribute["citation_list"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRecords_Lists_Ids_Existing_People_Linked_Back_And_What_Still_Needs_A_Link()
    {
        var handler = new TreeHandler
        {
            ExtraChanges = """
                [{ "_class": "Person", "handle": "h-mother", "type": "update", "old": {},
                   "new": { "_class": "Person", "handle": "h-mother", "gramps_id": "I0007",
                            "primary_name": { "_class": "Name", "first_name": "Maria",
                                              "type": { "_class": "NameType", "value": 2, "string": "" },
                                              "surname_list": [{ "_class": "Surname", "surname": "Petrova",
                                                                 "origintype": { "_class": "NameOriginType", "value": 1, "string": "" } }] },
                            "attribute_list": [{ "_class": "Attribute", "value": "MH:I7",
                                                 "type": { "_class": "AttributeType", "value": 0, "string": "RIN" } }],
                            "urls": [{ "_class": "Url", "path": "https://example.org", "type": { "_class": "UrlType", "value": 2, "string": "" } }],
                            "event_ref_list": [{ "_class": "EventRef", "ref": "e7",
                                                 "role": { "_class": "EventRoleType", "value": 1, "string": "" } }] } }]
                """
        };

        var result = await CreateRecords("""
            {
              "notes": [{ "text": "Read in 2026" }],
              "events": [{ "key": "birth", "eventType": "Birth" }, { "key": "death", "eventType": "Death" }],
              "people": [
                { "key": "ivan", "primaryName": "Ivan Petrov", "eventRefs": ["#birth"] },
                { "key": "anna", "primaryName": "Anna Petrova" }
              ],
              "families": [{ "fatherHandle": "#ivan", "motherHandle": "h-mother" }]
            }
            """, handler);

        var handles = handler.Body!.AsArray().Select(o => o!["handle"]!.GetValue<string>()).ToList();
        Assert.StartsWith("---\ntype: Transaction\naction: created\nrecords: 6\n---", result.ReplaceLineEndings("\n"));
        Assert.Contains($"Note: General — handle: {handles[0]} | gramps_id: N0001\n", result.ReplaceLineEndings("\n"));
        Assert.Contains($"Event: Birth — handle: {handles[1]} | gramps_id: E0002 | key: birth", result);
        Assert.Contains($"Person: Ivan Petrov — handle: {handles[3]} | gramps_id: I0004 | key: ivan", result);
        Assert.Contains($"Family: Unknown — handle: {handles[5]} | gramps_id: F0006", result);
        Assert.Equal("Unknown", handler.Body!.AsArray()[5]!["type"]!.GetValue<string>());
        Assert.Contains("Existing records that now link back to a new family (1):", result);
        Assert.Contains("Person: Maria Petrova — handle: h-mother | gramps_id: I0007", result);
        Assert.Contains("Not linked from any record of this call (2): notes[0], events[1] (key death).", result);
        Assert.Contains("people[1] (key anna) is in no new family", result);
        Assert.DoesNotContain("people[0]", result);
    }

    [Fact]
    public async Task CreateRecords_Saves_New_Tags_First_And_Links_Them_By_Key()
    {
        var handler = new TreeHandler { Tags = """[{ "handle": "h-old", "name": "Checked", "color": "#00ff00", "priority": 0 }]""" };

        var result = (await CreateRecords("""
            {
              "notes": [{ "text": "Read in 2026", "tagHandles": ["#todo", "h-old"] }],
              "people": [{ "key": "ivan", "primaryName": "Ivan Petrov", "tagHandles": ["#todo"] }],
              "tags": [{ "key": "todo", "name": " To check ", "color": "FF5733", "priority": 2 }, { "name": "Spare" }]
            }
            """, handler)).ReplaceLineEndings("\n");

        var body = handler.Body!.AsArray();
        Assert.Equal(["Tag", "Tag", "Note", "Person"], body.Select(o => o!["_class"]!.GetValue<string>()));
        var (todo, spare, note, person) = (body[0]!, body[1]!, body[2]!, body[3]!);
        Assert.Equal("To check", todo["name"]!.GetValue<string>());
        Assert.Equal("#ff5733", todo["color"]!.GetValue<string>());
        Assert.Equal(2, todo["priority"]!.GetValue<int>());
        Assert.Null(todo["gramps_id"]);
        Assert.Equal("#000000", spare["color"]!.GetValue<string>());
        var todoHandle = todo["handle"]!.GetValue<string>();
        Assert.Equal([todoHandle, "h-old"], note["tag_list"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal([todoHandle], person["tag_list"]!.AsArray().Select(t => t!.GetValue<string>()));

        Assert.Contains($"Tag: To check — handle: {todoHandle} | key: todo\n", result);
        Assert.Contains($"Tag: Spare — handle: {spare["handle"]!.GetValue<string>()}\n", result);
        Assert.Contains("Not linked from any record of this call (2): tags[1], notes[0].", result);
    }

    [Fact]
    public async Task CreateRecords_Refuses_A_Tag_Name_The_Tree_Already_Has()
    {
        var handler = new TreeHandler { Tags = """[{ "handle": "h-todo", "name": "To Check", "color": "#000000", "priority": 0 }]""" };

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords("""
            { "tags": [{ "key": "todo", "name": "to check" }], "notes": [{ "text": "x", "tagHandles": ["#todo"] }] }
            """, handler));

        Assert.Equal("Error: tags[0] (key todo): the tree already has a tag named \"To Check\" (handle h-todo); " +
                     "give that handle in tagHandles instead of creating the tag again.", error.Message);
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Reads_The_Tags_Only_When_It_Creates_One()
    {
        var handler = new TreeHandler();

        await CreateRecords("""{ "notes": [{ "text": "x", "tagHandles": ["h-old"] }] }""", handler);

        Assert.DoesNotContain("/api/tags/", handler.Requests);
    }

    [Theory]
    [InlineData("""{ "sources": [{ "key": "book", "title": "Book" }], "citations": [{ "sourceHandle": "#bok" }] }""",
        "Error: citations[0]: sourceHandle has \"#bok\", which is not a key of this call. Keys of source records in this call: #book.")]
    [InlineData("""{ "events": [{ "eventType": "Birth", "placeHandle": "#village" }] }""",
        "Error: events[0]: placeHandle has \"#village\", which is not a key of this call. No place of this call has a key.")]
    [InlineData("""{ "people": [{ "key": "ivan", "primaryName": "Ivan Petrov", "eventRefs": ["#maria"] }, { "key": "maria", "primaryName": "Maria Petrova" }] }""",
        "Error: people[0] (key ivan): eventRefs takes an event, but \"#maria\" is people[1], a person.")]
    [InlineData("""{ "events": [{ "eventType": "Birth", "mediaHandles": ["#scan"] }] }""",
        "Error: events[0]: mediaHandles has \"#scan\", but create_records does not create media: upload the file first with upload_media and give its handle.")]
    [InlineData("""{ "notes": [{ "text": "To do", "tagHandles": ["#todo"] }] }""",
        "Error: notes[0]: tagHandles has \"#todo\", which is not a key of this call. No tag of this call has a key.")]
    [InlineData("""{ "tags": [{ "key": "todo", "name": "To check" }], "events": [{ "eventType": "Birth", "citationHandles": ["#todo"] }] }""",
        "Error: events[0]: citationHandles takes a citation, but \"#todo\" is tags[0], a tag.")]
    [InlineData("""{ "tags": [{ "name": "  " }] }""",
        "Error: tags[0]: name is required")]
    [InlineData("""{ "tags": [{ "name": "To check", "color": "red" }] }""",
        "Error: tags[0]: color must be six hex digits RRGGBB")]
    [InlineData("""{ "tags": [{ "key": "a", "name": "To check" }, { "key": "b", "name": "to CHECK " }] }""",
        "Error: tags[1] (key b): tags[0] already creates a tag named \"to CHECK\"; tag names must differ.")]
    [InlineData("""{ "events": [{ "key": "b", "eventType": "Birth" }], "people": [{ "key": "#b", "primaryName": "Ivan Petrov" }] }""",
        "Error: people[0]: key \"b\" is already used by events[0]; keys must be unique in the call.")]
    [InlineData("""{ "notes": [{ "key": "my note", "text": "x" }] }""",
        "Error: notes[0]: key \"my note\" may hold only letters, digits, _ and -, up to 64.")]
    [InlineData("""{ "people": [null] }""",
        "Error: people[0] is empty; give each record as an object, or leave it out.")]
    [InlineData("{}",
        "Error: nothing to create. Give at least one record in tags, repositories, places, sources, notes, citations, events, people or families.")]
    public async Task CreateRecords_Rejects_A_Bad_Call_Before_Saving(string arguments, string expected)
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords(arguments, handler));

        Assert.StartsWith(expected, error.Message);
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Names_The_Record_A_Create_Tool_Check_Rejects()
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords("""
            { "events": [{ "key": "b", "eventType": "Birth" }, { "key": "d", "eventType": "Daeth" }] }
            """, handler));

        Assert.StartsWith("Error: events[1] (key d): Invalid event types 'Daeth'.", error.Message);
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Says_A_Call_Gramps_Web_Refuses_Saved_Nothing()
    {
        var handler = new TreeHandler
        {
            Refusal = (HttpStatusCode.UnprocessableEntity,
                """{"error": {"code": 422, "message": "Family references a person that does not exist: h-missing"}}""")
        };

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords("""
            { "families": [{ "fatherHandle": "h-missing" }] }
            """, handler));

        Assert.StartsWith("Gramps Web refused the call and saved none of the records. Gramps API error 422", error.Message);
        Assert.Contains("Family references a person that does not exist: h-missing", error.Message);
    }

    [Fact]
    public async Task CreateRecords_Takes_Up_To_50_Records()
    {
        var handler = new TreeHandler();
        var notes = string.Join(",", Enumerable.Repeat("""{ "text": "x" }""", RecordBatchTools.MaxRecords + 1));

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords($$"""{ "notes": [{{notes}}] }""", handler));

        Assert.StartsWith("Error: create_records takes up to 50 records per call, and this call has 51.", error.Message);
        Assert.Empty(handler.Posts);
    }

    [Fact]
    public async Task CreateRecords_Saves_None_Of_The_Records_When_A_New_Person_May_Already_Be_In_The_Tree()
    {
        var handler = new TreeHandler { People = $"[{DuplicatePeopleTests.Person("h-ivan", "I0012", "Иван", "Образцов", 1, birth: "1881")}]" };

        var result = (await CreateRecords(Ivan(birth: "1880"), handler)).ReplaceLineEndings("\n");

        Assert.Empty(handler.Posts);
        Assert.StartsWith("---\ntype: Transaction\naction: refused\nduplicates: 1\n---\n", result);
        Assert.Contains("Created none of the records: the tree may already have one of the new people.", result);
        Assert.Contains("people[0] (key ivan), Иван Образцов:\n  Possible duplicates (1):\n" +
                        "    • Образцов, Иван (I0012) [handle: h-ivan], b. 1881 — same name, born 1 year apart", result);
        Assert.Contains("people[0] (key ivan) notDuplicateOf: [\"I0012\"]", result);
    }

    [Fact]
    public async Task CreateRecords_Takes_The_Existing_Parents_Of_A_New_Family_For_The_Duplicate_Check()
    {
        var handler = new TreeHandler
        {
            People = $"[{DuplicatePeopleTests.Person("h-ivan", "I0012", "Иван", "Образцов", 1, fatherHandle: "h-petr")}]"
        };

        var result = await CreateRecords(Ivan(families: """[{ "fatherHandle": "h-petr", "childRefs": ["#ivan"] }]"""), handler);

        Assert.Empty(handler.Posts);
        Assert.Contains("same name and parents, no birth years to compare", result);
    }

    [Fact]
    public async Task CreateRecords_Saves_The_Records_And_Lists_Similar_People()
    {
        var handler = new TreeHandler { People = $"[{DuplicatePeopleTests.Person("h-ivan", "I0012", "Иван", "Образцов", 1, birth: "1884")}]" };

        var result = (await CreateRecords(Ivan(birth: "1880"), handler)).ReplaceLineEndings("\n");

        var person = handler.Body!.AsArray().Single(o => o!["_class"]!.GetValue<string>() == "Person")!;
        Assert.Equal(["/api/objects/"], handler.Posts);
        Assert.Contains("Similar people already in the tree, possibly the same as a new person:\n" +
                        $"  people[0] (key ivan), Иван Образцов — handle: {person["handle"]!.GetValue<string>()} (1):\n" +
                        "    • Образцов, Иван (I0012) [handle: h-ivan], b. 1884 — same name, born 4 years apart", result);
        Assert.Contains("merge_objects(objectType: \"person\"", result);
    }

    [Fact]
    public async Task CreateRecords_Passes_Over_The_People_In_NotDuplicateOf()
    {
        var handler = new TreeHandler { People = $"[{DuplicatePeopleTests.Person("h-ivan", "I0012", "Иван", "Образцов", 1, birth: "1880")}]" };

        var result = await CreateRecords(Ivan(birth: "1880", notDuplicateOf: """["i0012"]"""), handler);

        Assert.Equal(["/api/objects/"], handler.Posts);
        Assert.DoesNotContain("I0012", result);
    }

    /// <summary>
    /// Each record has the fields of its update tool, except those that only change a stored record, and every
    /// field is described.
    /// </summary>
    [Theory]
    [InlineData(typeof(PersonRecord), typeof(PersonTools), nameof(PersonTools.UpdatePerson), new[] { "notDuplicateOf" })]
    [InlineData(typeof(EventRecord), typeof(EventTools), nameof(EventTools.UpdateEvent), new string[0])]
    [InlineData(typeof(FamilyRecord), typeof(FamilyTools), nameof(FamilyTools.UpdateFamily), new string[0])]
    [InlineData(typeof(CitationRecord), typeof(CitationTools), nameof(CitationTools.UpdateCitation), new string[0])]
    [InlineData(typeof(NoteRecord), typeof(NoteTools), nameof(NoteTools.UpdateNote), new string[0])]
    [InlineData(typeof(SourceRecord), typeof(SourceTools), nameof(SourceTools.UpdateSource), new string[0])]
    [InlineData(typeof(RepositoryRecord), typeof(RepositoryTools), nameof(RepositoryTools.UpdateRepository), new string[0])]
    [InlineData(typeof(PlaceRecord), typeof(PlaceTools), nameof(PlaceTools.UpdatePlace), new string[0])]
    [InlineData(typeof(TagRecord), typeof(TagTools), nameof(TagTools.UpdateTag), new string[0])]
    public void Each_Record_Takes_The_Fields_Of_Its_Update_Tool(Type record, Type tools, string method, string[] createOnly)
    {
        string[] updateOnly = ["handle", "linkMode", "sort", "familyHandles", "parentFamilyHandles", "appendText", "replaceText", "withText"];
        var arguments = tools.GetMethod(method)!.GetParameters()
            .Where(p => p.ParameterType != typeof(GrampsApiClient))
            .Select(p => p.Name!)
            .Except(updateOnly)
            .Concat(createOnly)
            .Order();
        var fields = record.GetProperties()
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .OfType<string>()
            .Except(["key"])
            .Order();

        Assert.Equal(arguments, fields);
        Assert.All(record.GetProperties().Where(p => p.GetCustomAttribute<JsonPropertyNameAttribute>() != null),
            p => Assert.NotNull(p.GetCustomAttribute<DescriptionAttribute>()));
    }

    /// <summary>The descriptions of create_records and its records name no create tool but itself.</summary>
    [Fact]
    public void CreateRecords_Names_No_Other_Create_Tool()
    {
        var method = typeof(RecordBatchTools).GetMethod(nameof(RecordBatchTools.CreateRecords))!;
        var texts = method.GetParameters().Select(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description)
            .Append(method.GetCustomAttribute<DescriptionAttribute>()!.Description)
            .Concat(typeof(BatchRecord).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(BatchRecord)) || type == typeof(BatchRecord))
                .SelectMany(type => type.GetProperties())
                .Select(p => p.GetCustomAttribute<DescriptionAttribute>()?.Description))
            .OfType<string>();

        Assert.All(texts, text => Assert.DoesNotMatch(@"create_(?!records)", text));
    }

    /// <summary>create_records arguments for Иван Образцов (key ivan), born in <paramref name="birth"/> if given.</summary>
    private static string Ivan(string? birth = null, string? notDuplicateOf = null, string families = "[]")
    {
        var events = birth == null ? "[]" : $$"""[{ "key": "birth", "eventType": "Birth", "date": "{{birth}}" }]""";
        var eventRefs = birth == null ? "[]" : """["#birth"]""";
        return $$"""
            {
              "events": {{events}},
              "people": [{ "key": "ivan", "primaryName": { "given": "Иван", "surname": "Образцов" }, "gender": "Male",
                           "eventRefs": {{eventRefs}}, "notDuplicateOf": {{notDuplicateOf ?? "null"}} }],
              "families": {{families}}
            }
            """;
    }

    private static Task<string> CreateRecords(string json, TreeHandler handler) =>
        CreateRecordsCall.RunAsync(json, CreateClient(handler));

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://create-records.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves the type vocabularies and bodies by path and query (404 otherwise), records the POST body and answers
    /// it as Gramps Web does: an "add" change per object with the next Gramps ID, then <see cref="ExtraChanges"/>.
    /// </summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string>? bodies = null) : HttpMessageHandler
    {
        private const string DefaultTypes = """
            {
              "event_types": ["Unknown", "Birth", "Death", "Marriage", "Baptism", "Burial"],
              "event_role_types": ["Unknown", "Primary", "Family", "Witness"],
              "family_relation_types": ["Married", "Unmarried", "Civil Union", "Unknown"],
              "child_reference_types": ["None", "Birth", "Adopted"],
              "name_types": ["Unknown", "Also Known As", "Birth Name", "Married Name"],
              "note_types": ["Unknown", "General", "Research"],
              "repository_types": ["Unknown", "Archive", "Library"],
              "url_types": ["Unknown", "E-mail", "Web Home", "Web Search", "FTP"],
              "source_media_types": ["Unknown", "Book", "Manuscript"],
              "place_types": ["Unknown", "Village", "City"]
            }
            """;

        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<string> _posts = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public IReadOnlyList<string> Posts => [.. _posts];

        public JsonNode? Body { get; private set; }

        public string ExtraChanges { get; init; } = "[]";

        /// <summary>When set, the POST is answered with this status and body instead.</summary>
        public (HttpStatusCode Status, string Body)? Refusal { get; init; }

        /// <summary>The answer to the duplicate check's people searches.</summary>
        public string People { get; init; } = "[]";

        /// <summary>The tags of the tree, read when the call creates tags.</summary>
        public string Tags { get; init; } = "[]";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Post)
            {
                _posts.Enqueue(path);
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (Refusal is var (status, refusal))
                    return Json(refusal, status);
                var changes = CreateRecordsCall.Changes(Body!);
                foreach (var change in JsonNode.Parse(ExtraChanges)!.AsArray())
                    changes.Add(change!.DeepClone());
                return Json(changes.ToJsonString(), HttpStatusCode.Created);
            }

            return path switch
            {
                "/api/types/default/" => Json(DefaultTypes),
                "/api/types/custom/" => Json("{}"),
                "/api/tags/" => Json(Tags),
                _ when path.StartsWith("/api/people/?rules=", StringComparison.Ordinal) => Json(People),
                _ => bodies?.TryGetValue(path, out var body) == true ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
