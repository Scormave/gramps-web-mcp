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
        var handler = new TreeHandler();

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
        Assert.Equal("I0007", family["mother_handle"]!.GetValue<string>());
        Assert.Equal(handles[4], family["event_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.Equal(handles[5], family["child_ref_list"]![0]!["ref"]!.GetValue<string>());
        Assert.DoesNotContain("\"#", handler.Body.ToJsonString().Replace("\"#1 in the book\"", ""));
        Assert.Equal("State Archive", repository["name"]!.GetValue<string>());
        Assert.Equal("Marriage", marriage["type"]!.GetValue<string>());
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
        Assert.Contains($"Family: Married — handle: {handles[5]} | gramps_id: F0006", result);
        Assert.Contains("Existing records that now link back to a new family (1):", result);
        Assert.Contains("Person: Maria Petrova — handle: h-mother | gramps_id: I0007", result);
        Assert.Contains("Not linked from any record of this call (2): notes[0], events[1] (key death).", result);
        Assert.Contains("people[1] (key anna) is in no new family", result);
        Assert.DoesNotContain("people[0]", result);
    }

    [Theory]
    [InlineData("""{ "sources": [{ "key": "book", "title": "Book" }], "citations": [{ "sourceHandle": "#bok" }] }""",
        "Error: citations[0]: sourceHandle has \"#bok\", which is not a key of this call. Keys of source records in this call: #book.")]
    [InlineData("""{ "events": [{ "eventType": "Birth", "placeHandle": "#village" }] }""",
        "Error: events[0]: placeHandle has \"#village\", which is not a key of this call. No place of this call has a key.")]
    [InlineData("""{ "people": [{ "key": "ivan", "primaryName": "Ivan Petrov", "eventRefs": ["#maria"] }, { "key": "maria", "primaryName": "Maria Petrova" }] }""",
        "Error: people[0] (key ivan): eventRefs takes an event, but \"#maria\" is people[1], a person.")]
    [InlineData("""{ "events": [{ "eventType": "Birth", "mediaHandles": ["#scan"] }] }""",
        "Error: events[0]: mediaHandles has \"#scan\", but create_records does not create media: create it first with create_media and give its handle.")]
    [InlineData("""{ "notes": [{ "text": "To do", "tagHandles": ["#todo"] }] }""",
        "Error: notes[0]: tagHandles has \"#todo\", but create_records does not create tags: create it first with create_tag and give its handle.")]
    [InlineData("""{ "events": [{ "key": "b", "eventType": "Birth" }], "people": [{ "key": "#b", "primaryName": "Ivan Petrov" }] }""",
        "Error: people[0]: key \"b\" is already used by events[0]; keys must be unique in the call.")]
    [InlineData("""{ "notes": [{ "key": "my note", "text": "x" }] }""",
        "Error: notes[0]: key \"my note\" may hold only letters, digits, _ and -, up to 64.")]
    [InlineData("""{ "people": [null] }""",
        "Error: people[0] is empty; give each record as an object, or leave it out.")]
    [InlineData("{}",
        "Error: nothing to create. Give at least one record in repositories, places, sources, notes, citations, events, people or families.")]
    public async Task CreateRecords_Rejects_A_Bad_Call_Before_Saving(string arguments, string expected)
    {
        var handler = new TreeHandler();

        var error = await Assert.ThrowsAsync<McpException>(() => CreateRecords(arguments, handler));

        Assert.Equal(expected, error.Message);
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

    [Theory]
    [InlineData(typeof(PersonRecord), typeof(PersonTools), nameof(PersonTools.CreatePerson))]
    [InlineData(typeof(EventRecord), typeof(EventTools), nameof(EventTools.CreateEvent))]
    [InlineData(typeof(FamilyRecord), typeof(FamilyTools), nameof(FamilyTools.CreateFamily))]
    [InlineData(typeof(CitationRecord), typeof(CitationTools), nameof(CitationTools.CreateCitation))]
    [InlineData(typeof(NoteRecord), typeof(NoteTools), nameof(NoteTools.CreateNote))]
    [InlineData(typeof(SourceRecord), typeof(SourceTools), nameof(SourceTools.CreateSource))]
    [InlineData(typeof(RepositoryRecord), typeof(RepositoryTools), nameof(RepositoryTools.CreateRepository))]
    [InlineData(typeof(PlaceRecord), typeof(PlaceTools), nameof(PlaceTools.CreatePlace))]
    public void Each_Record_Takes_The_Arguments_Of_Its_Create_Tool(Type record, Type tools, string method)
    {
        var arguments = tools.GetMethod(method)!.GetParameters()
            .Where(p => p.ParameterType != typeof(GrampsApiClient))
            .Select(p => p.Name!)
            .Except(["familyHandles", "parentFamilyHandles"])
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

    private static Task<string> CreateRecords(string json, TreeHandler handler)
    {
        var arguments = JsonNode.Parse(json)!.AsObject();
        T[]? Records<T>(string name) => arguments[name]?.Deserialize<T[]>();
        return RecordBatchTools.CreateRecords(
            Records<RepositoryRecord>("repositories"), Records<PlaceRecord>("places"), Records<SourceRecord>("sources"),
            Records<NoteRecord>("notes"), Records<CitationRecord>("citations"), Records<EventRecord>("events"),
            Records<PersonRecord>("people"), Records<FamilyRecord>("families"), CreateClient(handler));
    }

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
              "source_media_types": ["Unknown", "Book", "Manuscript"],
              "place_types": ["Unknown", "Village", "City"]
            }
            """;

        private static readonly Dictionary<string, string> Prefixes = new()
        {
            ["Person"] = "I", ["Family"] = "F", ["Event"] = "E", ["Place"] = "P", ["Source"] = "S",
            ["Citation"] = "C", ["Repository"] = "R", ["Note"] = "N"
        };

        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<string> _posts = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public IReadOnlyList<string> Posts => [.. _posts];

        public JsonNode? Body { get; private set; }

        public string ExtraChanges { get; init; } = "[]";

        /// <summary>When set, the POST is answered with this status and body instead.</summary>
        public (HttpStatusCode Status, string Body)? Refusal { get; init; }

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
                var changes = new JsonArray(Body!.AsArray().Select((o, i) =>
                {
                    var grampsClass = o!["_class"]!.GetValue<string>();
                    var handle = o["handle"]!.GetValue<string>();
                    return (JsonNode)new JsonObject
                    {
                        ["_class"] = grampsClass, ["handle"] = handle, ["type"] = "add", ["old"] = null,
                        ["new"] = new JsonObject
                        {
                            ["_class"] = grampsClass, ["handle"] = handle, ["gramps_id"] = $"{Prefixes[grampsClass]}{i + 1:D4}"
                        }
                    };
                }).ToArray());
                foreach (var change in JsonNode.Parse(ExtraChanges)!.AsArray())
                    changes.Add(change!.DeepClone());
                return Json(changes.ToJsonString(), HttpStatusCode.Created);
            }

            return path switch
            {
                "/api/types/default/" => Json(DefaultTypes),
                "/api/types/custom/" => Json("{}"),
                _ => bodies?.TryGetValue(path, out var body) == true ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
