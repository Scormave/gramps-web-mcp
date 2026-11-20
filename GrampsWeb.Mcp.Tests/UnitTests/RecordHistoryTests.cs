using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;
using static GrampsWeb.Mcp.Formatters.RecordHistoryFormatter;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class RecordHistoryTests
{
    private const string EmptyDate =
        """{"_class":"Date","calendar":0,"modifier":0,"quality":0,"dateval":[0,0,0,false],"text":"","sortval":0,"newyear":0,"format":null}""";

    private static string EventRef(string handle, int role) =>
        $$"""{"_class":"EventRef","ref":"{{handle}}","role":{"_class":"EventRoleType","value":{{role}},"string":""},"private":false,"citation_list":[],"note_list":[],"attribute_list":[]}""";

    private static string Person(string firstName, int gender, string eventRefs, string families, string notes,
        int change = 100, string date = EmptyDate) => $$"""
        {
          "_class": "Person", "handle": "p1", "change": {{change}}, "private": false, "gramps_id": "I0004",
          "gender": {{gender}},
          "primary_name": {
            "_class": "Name", "first_name": "{{firstName}}", "date": {{date}},
            "type": {"_class": "NameType", "value": 2, "string": ""},
            "surname_list": [{"_class": "Surname", "surname": "Smith", "prefix": "", "primary": true, "connector": ""}]
          },
          "event_ref_list": [{{eventRefs}}],
          "family_list": [{{families}}], "note_list": [{{notes}}], "tag_list": [],
          "birth_ref_index": 0, "death_ref_index": -1
        }
        """;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>?> RoleLabels =
        new Dictionary<string, IReadOnlyList<string>?>
        {
            ["event_role_types"] = ["Custom", "Primary", "Clergy", "Celebrant", "Aide", "Bride", "Groom", "Witness"]
        };

    private static string Lines(IReadOnlyList<FieldDiff> diffs, IReadOnlyDictionary<string, string>? labels = null,
        int transType = 1)
    {
        var change = new GrampsObjectChange { TransType = transType, TransactionId = 7 };
        var text = FormatHistory("Person I0004: Smith, John [handle: p1]", [change], [diffs], 1,
            labels ?? new Dictionary<string, string>(), RoleLabels);
        return text.Replace("\r\n", "\n");
    }

    [Fact]
    public void Compare_Lists_Only_The_Fields_That_Changed()
    {
        var before = Json(Person("John", 1, EventRef("e1", 1), "\"f1\"", "\"n1\",\"n2\""));
        var after = Json(Person("Johann", 1, EventRef("e1", 7), "\"f1\",\"f2\"", "\"n2\",\"n1\"", change: 200));

        var diffs = Compare(before, after);

        Assert.Equal(
            ["primary_name.first_name", "event_ref_list.role", "family_list", "note_list"],
            diffs.Select(d => string.Join(".", d.Path.Select(step => step.Name))));
        Assert.Equal([DiffKind.Changed, DiffKind.Changed, DiffKind.Added, DiffKind.Reordered], diffs.Select(d => d.Kind));
        Assert.Equal("e1", diffs[1].Path[0].Ref);
    }

    [Fact]
    public void Update_Shows_Before_And_After_With_Linked_Records_Named()
    {
        var before = Json(Person("John", 1, EventRef("e1", 1), "\"f1\"", "\"n1\",\"n2\""));
        var after = Json(Person("Johann", 0, EventRef("e1", 7), "\"f1\",\"f2\"", "\"n2\",\"n1\""));
        var labels = new Dictionary<string, string> { ["e1"] = "Birth, 1890", ["f2"] = "Smith, John and Brown, Mary" };

        var text = Lines(Compare(before, after), labels);

        Assert.Contains("1. Updated [transaction: 7]\n" +
                        "   gender: Male → Female\n" +
                        "   primary_name.first_name: \"John\" → \"Johann\"\n" +
                        "   event_ref_list[Birth, 1890].role: Primary → Witness\n" +
                        "   family_list: added Smith, John and Brown, Mary [handle: f2]\n" +
                        "   note_list: reordered\n", text);
        Assert.Equal(RoleLabels.Keys, TypeCategoriesOf(Compare(before, after)));
        Assert.Equal([("events", "e1"), ("families", "f2")], LinkedRecords(Compare(before, after)));
    }

    [Fact]
    public void Added_And_Removed_Links_Keep_Their_Details()
    {
        var before = Json(Person("John", 1, EventRef("e1", 1), "", ""));
        var after = Json(Person("John", 1, EventRef("e2", 7), "", ""));

        var text = Lines(Compare(before, after));

        Assert.Contains("   event_ref_list: removed [handle: e1] (role: Primary)\n" +
                        "   event_ref_list: added [handle: e2] (role: Witness)\n", text);
    }

    [Fact]
    public void Dates_Compare_And_Show_As_One_Value()
    {
        const string date1890 =
            """{"_class":"Date","calendar":0,"modifier":0,"quality":0,"dateval":[12,3,1890,false],"text":"","sortval":2411074,"newyear":0,"format":null}""";
        var before = Json(Person("John", 1, "", "", "", date: EmptyDate));
        var after = Json(Person("John", 1, "", "", "", date: date1890));

        var diffs = Compare(before, after);

        var diff = Assert.Single(diffs);
        Assert.Equal("date", diff.Field);
        Assert.Matches(@"primary_name\.date: \(no date\) → .*1890", Lines(diffs));
    }

    [Fact]
    public void An_Added_Record_Lists_Its_Fields_That_Hold_A_Value()
    {
        var added = Json(Person("John", 1, EventRef("e1", 1), "", ""));

        var text = Lines(Compare(Json("{}"), added), transType: 0);

        Assert.Contains("1. Added [transaction: 7]\n" +
                        "   gramps_id: \"I0004\"\n" +
                        "   gender: Male\n" +
                        "   primary_name.first_name: \"John\"\n" +
                        "   primary_name.type: 2\n" +
                        "   primary_name.surname_list: {surname: \"Smith\", primary: true}\n" +
                        "   event_ref_list: [handle: e1] (role: Primary)\n" +
                        "   birth_ref_index: 0\n", text);
        Assert.DoesNotContain("death_ref_index", text);
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("primary_name.date", text);
    }

    [Fact]
    public void An_Added_Record_Leaves_Out_Default_Name_Settings()
    {
        var added = Json("""
            {
              "_class": "Person", "gramps_id": "I0004",
              "primary_name": {
                "_class": "Name", "first_name": "John", "sort_as": 0, "display_as": 0,
                "surname_list": [{"_class": "Surname", "surname": "Smith", "primary": true,
                                  "origintype": {"_class": "NameOriginType", "value": 1, "string": ""}}]
              }
            }
            """);
        var labels = new Dictionary<string, IReadOnlyList<string>?>
        {
            ["name_origin_types"] = ["Custom", "", "Inherited", "Given"]
        };
        var change = new GrampsObjectChange { TransType = 0, TransactionId = 7 };

        var text = FormatHistory("Person I0004 [handle: p1]", [change], [Compare(null, added)], 1,
            new Dictionary<string, string>(), labels).Replace("\r\n", "\n");

        Assert.Contains("   primary_name.surname_list: {surname: \"Smith\", primary: true}\n", text);
        Assert.DoesNotContain("sort_as", text);
        Assert.DoesNotContain("display_as", text);
        Assert.DoesNotContain("origintype", text);
    }

    [Fact]
    public void A_Deleted_Record_Lists_What_It_Held()
    {
        var deleted = Json(Person("John", 2, "", "", "\"n1\""));

        var text = Lines(Compare(deleted, null), new Dictionary<string, string> { ["n1"] = "[General] Born at home" },
            transType: 2);

        Assert.Contains("1. Deleted [transaction: 7]\n", text);
        Assert.Contains("   gender: Unknown\n", text);
        Assert.Contains("   note_list: [General] Born at home [handle: n1]\n", text);
    }

    [Fact]
    public void Long_Text_Shows_Where_It_Changed()
    {
        var start = new string('a', 100);
        var end = new string('z', 100);
        var before = Json($$$"""{"_class":"Note","text":{"_class":"StyledText","string":"{{{start}}} born 1890 {{{end}}}","tags":[]}}""");
        var after = Json($$$"""{"_class":"Note","text":{"_class":"StyledText","string":"{{{start}}} born 1891 {{{end}}}","tags":[]}}""");

        var text = Lines(Compare(before, after));

        Assert.Contains("   text: 211 → 211 characters, from character 110: " +
                        $"\"…{new string('a', 11)} born 1890 {new string('z', 19)}…\" → \"…{new string('a', 11)} born 1891 {new string('z', 19)}…\"",
            text);
    }

    [Fact]
    public void Text_Formatting_Alone_Is_Named_As_Such()
    {
        var before = Json("""{"text":{"_class":"StyledText","string":"Born at home","tags":[]}}""");
        var after = Json("""{"text":{"_class":"StyledText","string":"Born at home","tags":[{"name":"bold","ranges":[[0,4]]}]}}""");

        var text = Lines(Compare(before, after));

        Assert.Contains("   text: text formatting changed\n", text);
    }

    [Fact]
    public void An_Update_Without_A_Changed_Field_Says_So()
    {
        var person = Person("John", 1, "", "", "");

        var text = Lines(Compare(Json(person), Json(person.Replace("\"change\": 100", "\"change\": 200"))));

        Assert.Contains("1. Updated [transaction: 7]\n   (no field changed)\n", text);
    }

    [Fact]
    public void Many_Changed_Fields_Are_Counted_Past_The_Limit()
    {
        var before = Json("{" + string.Join(",", Enumerable.Range(1, 15).Select(i => $"\"f{i}\": \"a\"")) + "}");
        var after = Json("{" + string.Join(",", Enumerable.Range(1, 15).Select(i => $"\"f{i}\": \"b\"")) + "}");

        var text = Lines(Compare(before, after));

        Assert.Contains("   f12: \"a\" → \"b\"\n   … (+3 more fields)\n", text);
        Assert.DoesNotContain("f13", text);
    }

    [Fact]
    public void A_Record_Without_History_Says_Where_Edits_Are_Kept()
    {
        var text = FormatHistory("Person I0004 [handle: p1]", [], [], 0,
            new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>?>());

        Assert.StartsWith("RECORD HISTORY\nPerson I0004 [handle: p1]\n", text.Replace("\r\n", "\n"));
        Assert.Contains("No changes of this record are in the history", text);
        Assert.Contains("Gramps desktop", text);
    }

    // ── The tool ──────────────────────────────────────────────────────────────

    private const string HistoryPath =
        "/api/transactions/history/objects/Person/p1?page=1&pagesize=20&sort=-id&old=true&new=true";

    private static string History() => $$$"""
        [
          {
            "id": 2, "obj_class": "Person", "trans_type": 1, "obj_handle": "p1", "ref_handle": null,
            "timestamp": 1759242125.5,
            "old_data": {{{Person("John", 1, EventRef("e1", 1), "", "")}}},
            "new_data": {{{Person("John", 1, EventRef("e1", 7), "", "")}}},
            "connection": {"id": 9, "timestamp": 1759242120.0, "user": {"name": "jdoe", "full_name": "Jane Doe"}},
            "transaction_id": 12
          },
          {
            "id": 1, "obj_class": "Person", "trans_type": 0, "obj_handle": "p1", "ref_handle": null,
            "timestamp": 1759242000,
            "old_data": {},
            "new_data": {{{Person("John", 1, EventRef("e1", 1), "", "")}}},
            "connection": {"id": 8, "timestamp": 1759242000, "user": null},
            "transaction_id": 11
          }
        ]
        """;

    [Fact]
    public async Task GetRecentChanges_With_A_Gramps_Id_Shows_That_Records_History()
    {
        using var handler = new ServerHandler(new()
        {
            ["/api/people/?gramps_id=I0004&pagesize=1"] = """[{"handle": "p1", "gramps_id": "I0004"}]""",
            [HistoryPath] = History(),
            ["/api/people/p1?profile=self"] = """{"handle": "p1", "gramps_id": "I0004", "profile": {"handle": "p1", "name_display": "Smith, John"}}""",
            ["/api/events/e1?profile=self"] = """
                {"handle": "e1", "gramps_id": "E0001", "type": "Birth", "description": "",
                 "date": {"dateval": [0, 0, 0, false], "text": ""}, "profile": {"type": "Birth", "date": "1890"}}
                """,
            ["/api/types/default/event_role_types/map"] = """{"-1": "Unknown", "0": "Custom", "1": "Primary", "7": "Witness"}""",
            ["/api/types/default/name_types/map"] = """{"-1": "Unknown", "0": "Custom", "1": "Also Known As", "2": "Birth Name"}""",
        }, total: 2);

        var result = (await SystemTools.GetRecentChanges(identifier: "I0004", client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Contains(HistoryPath, handler.Paths);
        Assert.StartsWith("RECORD HISTORY (2 of 2 changes, newest first)\n" +
                          "Person I0004: Smith, John [handle: p1]\n", result);
        Assert.Contains("1. 2025-09-30 14:22:05 UTC — Updated — by Jane Doe [transaction: 12]\n" +
                        "   event_ref_list[", result);
        Assert.Contains("].role: Primary → Witness\n", result);
        Assert.Contains("2. 2025-09-30 14:20:00 UTC — Added [transaction: 11]\n", result);
        Assert.Contains("   primary_name.type: Birth Name\n", result);
    }

    [Fact]
    public async Task GetRecentChanges_Names_A_Deleted_Record_From_Its_Stored_Data()
    {
        const string eventPath =
            "/api/transactions/history/objects/Event/e9-handle-0000001?page=1&pagesize=20&sort=-id&old=true&new=true";
        using var handler = new ServerHandler(new()
        {
            [eventPath] = """
                [{"id": 1, "obj_class": "Event", "trans_type": 2, "obj_handle": "e9-handle-0000001", "timestamp": 1759242125.5,
                  "old_data": {"_class": "Event", "handle": "e9-handle-0000001", "gramps_id": "E0009", "description": "Moved to town",
                               "type": {"_class": "EventType", "value": 0, "string": "Residence change"}},
                  "new_data": {}, "transaction_id": 5}]
                """,
        });

        var result = (await SystemTools.GetRecentChanges(identifier: "e9-handle-0000001", objectType: "event", client: CreateClient(handler)))
            .Replace("\r\n", "\n");

        Assert.Contains("Event E0009: ", result);
        Assert.Contains("[handle: e9-handle-0000001]\n", result);
        Assert.Contains("1. 2025-09-30 14:22:05 UTC — Deleted [transaction: 5]\n", result);
        Assert.Contains("   type: Residence change\n", result);
        Assert.Contains("   description: \"Moved to town\"\n", result);
    }

    [Fact]
    public async Task GetRecentChanges_Explains_A_Server_Without_Record_History()
    {
        using var handler = new ServerHandler(new()
        {
            ["/api/people/?gramps_id=I0004&pagesize=1"] = """[{"handle": "p1", "gramps_id": "I0004"}]""",
        });

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SystemTools.GetRecentChanges(identifier: "I0004", client: CreateClient(handler)));

        Assert.Contains("Gramps Web API 3.22 or later", ex.Message);
    }

    [Fact]
    public async Task GetRecentChanges_Reports_A_Gramps_Id_No_Record_Has()
    {
        using var handler = new ServerHandler(new() { ["/api/people/?gramps_id=I0999&pagesize=1"] = "[]" });

        var result = await SystemTools.GetRecentChanges(identifier: "I0999", client: CreateClient(handler));

        Assert.StartsWith("Person not found: I0999", result);
        Assert.Contains("keeps its history under its handle", result);
        Assert.DoesNotContain(handler.Paths, path => path.StartsWith("/api/transactions/"));
    }

    [Fact]
    public async Task GetRecentChanges_Rejects_ObjectType_Without_Identifier()
    {
        using var handler = new ServerHandler(new());

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SystemTools.GetRecentChanges(objectType: "person", client: CreateClient(handler)));

        Assert.Contains("objectType is used only with identifier", ex.Message);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task GetRecentChanges_Rejects_A_Handle_Without_ObjectType()
    {
        using var handler = new ServerHandler(new());

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SystemTools.GetRecentChanges(identifier: "p1", client: CreateClient(handler)));

        Assert.Contains("objectType is required", ex.Message);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        // A host of its own, so Gramps IDs resolved here stay out of other tests' handle cache.
        var config = new GrampsConfig("https://record-history.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>Answers the given paths, 404 for others, and records every request path but the token's.</summary>
    private sealed class ServerHandler(Dictionary<string, string> responses, int? total = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            lock (Paths)
                Paths.Add(path);
            if (!responses.TryGetValue(path, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var response = JsonResponse(body);
            if (total.HasValue && path.StartsWith("/api/transactions/", StringComparison.Ordinal))
                response.Headers.Add("X-Total-Count", total.Value.ToString());
            return Task.FromResult(response);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
