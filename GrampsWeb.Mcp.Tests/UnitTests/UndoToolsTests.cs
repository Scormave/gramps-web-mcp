using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class UndoToolsTests
{
    private const string HistoryPath = "/api/transactions/history/5";
    private const string CheckPath = "/api/transactions/history/5/undo";
    private const string NoConflicts =
        """{"transaction_id":5,"can_undo_without_force":true,"total_changes":2,"conflicts_count":0,"conflicts":[]}""";

    // Transaction 5 added note n1 and edited person p1, whose earlier version links event e1.
    private const string AddNoteEditPerson =
        """
        {
          "id": 5, "description": "Add Note", "undo": false, "timestamp": 1759242125.5,
          "connection": {"user": {"name": "jdoe", "full_name": "Jane Doe"}},
          "changes": [
            {"id": 1, "obj_class": "Note", "trans_type": 0, "obj_handle": "n1", "ref_handle": null, "old_data": null},
            {"id": 2, "obj_class": "7", "trans_type": 0, "obj_handle": "p1", "ref_handle": "n1", "old_data": null},
            {"id": 3, "obj_class": "Person", "trans_type": 1, "obj_handle": "p1", "ref_handle": null,
             "old_data": {"_class": "Person", "handle": "p1", "gramps_id": "I0001",
                          "event_ref_list": [{"_class": "EventRef", "ref": "e1", "note_list": []}], "note_list": []}}
          ]
        }
        """;

    // Transaction 5 deleted note n1.
    private const string DeleteNote =
        """
        {
          "id": 5, "description": "Delete Note", "undo": false, "timestamp": 1759242125.5,
          "changes": [
            {"id": 1, "obj_class": "Note", "trans_type": 2, "obj_handle": "n1", "ref_handle": null,
             "old_data": {"_class": "Note", "handle": "n1", "gramps_id": "N0001",
                          "text": {"_class": "StyledText", "string": "Born in Tver", "tags": []},
                          "type": {"_class": "NoteType", "value": 1, "string": ""}, "tag_list": []}}
          ]
        }
        """;

    [Fact]
    public async Task DryRun_ListsTheEffects_AndWritesNothing()
    {
        using var handler = AddNoteEditPersonServer();

        var result = await Undo(handler, dryRun: true);

        Assert.Contains("action: checked", result);
        Assert.Contains("conflicts: 0", result);
        Assert.Contains("Nothing was changed. Undoing transaction 5 (", result);
        Assert.Contains("   Delete Note: [General] A new note [handle: n1]", result);
        Assert.Contains("   Restore the earlier version of Person [handle: p1]", result);
        Assert.Contains("No conflicts", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_PostsWithMessage_AndNamesTheUndoTransaction()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id",
            """[{"id": 6, "description": "Undo transaction 5: Add Note", "changes": []}, {"id": 5, "description": "Add Note"}]""");

        var result = await Undo(handler);

        var post = Assert.Single(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
        Assert.Equal($"POST {CheckPath}?message=Undo transaction 5: Add Note", post);
        Assert.Contains("action: undone", result);
        Assert.Contains("undo_transaction: 6", result);
        Assert.Contains("   Deleted Note: [General] A new note [handle: n1]", result);
        Assert.Contains("   Restored the earlier version of Person [handle: p1]", result);
        Assert.Contains("undo_change(transactionId: 6) reverts it", result);
    }

    [Fact]
    public async Task Undo_FindsItsTransactionByRecords_WhenTheServerIgnoresTheMessage()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id",
            """
            [
              {"id": 7, "description": "Edit Event", "changes": [{"obj_class": "Event", "trans_type": 1, "obj_handle": "e1"}]},
              {"id": 6, "description": "Undo", "changes": [
                {"obj_class": "Person", "trans_type": 1, "obj_handle": "p1"},
                {"obj_class": "7", "trans_type": 2, "obj_handle": "p1", "ref_handle": "n1"},
                {"obj_class": "Note", "trans_type": 2, "obj_handle": "n1"}]}
            ]
            """);

        var result = await Undo(handler);

        Assert.Contains("undo_transaction: 6", result);
    }

    [Fact]
    public async Task Undo_RefusesWhatGrampsWebReportsAsChanged()
    {
        using var handler = AddNoteEditPersonServer(check:
            """
            {"transaction_id": 5, "can_undo_without_force": false, "total_changes": 2, "conflicts_count": 1,
             "conflicts": [{"change_index": 0, "object_class": "Note", "handle": "n1", "conflict_type": "object_changed",
                            "description": "Object has been modified since this transaction"}]}
            """);

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("conflicts: 1", result);
        Assert.Contains("Note: [General] A new note [handle: n1] was changed or deleted after this transaction added it.", result);
        Assert.Contains("newest first", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "conflicts: 0", "No conflicts")]
    [InlineData(2, "conflicts: 1", "Person [handle: p1] was changed or deleted after this transaction added it.")]
    public async Task Undo_CountsARecordChangedTwiceInTheTransactionOnce(int personConflicts, string header, string expected)
    {
        // Transaction 5 added person p1, then family f1, which linked p1 back in the same transaction. Gramps Web
        // compares both changes of p1 with p1 now, so its first change conflicts even when nobody changed p1 later.
        using var handler = new UndoServer();
        handler.Get($"{HistoryPath}?old=true",
            """
            {
              "id": 5, "description": "Add multiple objects", "undo": false, "timestamp": 1759242125.5,
              "changes": [
                {"id": 1, "obj_class": "Person", "trans_type": 0, "obj_handle": "p1", "old_data": null},
                {"id": 2, "obj_class": "Person", "trans_type": 1, "obj_handle": "p1",
                 "old_data": {"_class": "Person", "handle": "p1", "gramps_id": "I0001", "family_list": []}},
                {"id": 3, "obj_class": "Family", "trans_type": 0, "obj_handle": "f1", "old_data": null}
              ]
            }
            """);
        var conflict = """{"object_class": "Person", "handle": "p1", "conflict_type": "object_changed"}""";
        handler.Get(CheckPath,
            $$"""{"transaction_id": 5, "conflicts": [{{string.Join(", ", Enumerable.Repeat(conflict, personConflicts))}}]}""");
        handler.Get("/api/people/p1?keys=handle,backlinks&backlinks=true", """{"handle": "p1", "backlinks": {"family": ["f1"]}}""");

        var result = await Undo(handler, dryRun: true);

        Assert.Contains(header, result);
        Assert.Contains(expected, result);
        Assert.Single(result.Split('\n'), line => line.Contains("[handle: p1]") && line.StartsWith("   "));
        Assert.Contains("   Delete Person [handle: p1]", result);
        Assert.DoesNotContain("Restore the earlier version", result);
    }

    [Fact]
    public async Task Undo_RefusesToDeleteARecordOthersNowLink()
    {
        using var handler = AddNoteEditPersonServer(noteBacklinks: """{"person": ["p1", "p9"]}""");
        handler.Get("/api/people/?handles=p1,p9&profile=self&page=1&pagesize=2",
            """[{"handle": "p9", "gramps_id": "I0009", "profile": {"handle": "p9", "name_display": "Smith, Anna"}}]""");

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("Note: [General] A new note [handle: n1] is now linked from Person: Smith, Anna [handle: p9], " +
                        "which this transaction did not change", result);
        Assert.DoesNotContain("[handle: p1], which", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_RefusesToRestoreAVersionLinkingAMissingRecord()
    {
        using var handler = AddNoteEditPersonServer(eventExists: false);

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("The version of Person [handle: p1] to restore links Event [handle: e1], which no longer exist", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_WithForce_GoesPastConflicts_AndListsThem()
    {
        using var handler = AddNoteEditPersonServer(eventExists: false);
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id", "[]");

        var result = await Undo(handler, force: true);

        var post = Assert.Single(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
        Assert.EndsWith("&force=true", post);
        Assert.Contains("action: undone", result);
        Assert.Contains("Forced past conflicts (1):", result);
        Assert.Contains("get_recent_changes lists this undo", result);
    }

    [Fact]
    public async Task Undo_ReportsARecordChangedBetweenTheCheckAndTheUndo()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.InternalServerError, """{"error": {"code": 500, "message": "Object has changed"}}""");

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("Gramps Web refused to undo transaction 5", result);
        Assert.DoesNotContain("before 3.23", result);
    }

    [Fact]
    public async Task Undo_FollowsTheBackgroundTask()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.Accepted, """{"task": {"href": "/api/tasks/t1", "id": "t1"}}""");
        handler.GetSequence("/api/tasks/t1", """{"state": "PENDING"}""", """{"state": "SUCCESS", "result_object": null}""");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id",
            """[{"id": 6, "description": "Undo transaction 5: Add Note"}]""");

        var result = await Undo(handler);

        Assert.Equal(2, handler.Requests.Count(r => r == "GET /api/tasks/t1"));
        Assert.Contains("action: undone", result);
        Assert.Contains("undo_transaction: 6", result);
    }

    [Fact]
    public async Task Undo_ReportsPending_WhenTheTaskOutlastsTheWait()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.Accepted, """{"task": {"href": "/api/tasks/t1", "id": "t1"}}""");
        handler.Get("/api/tasks/t1", """{"state": "STARTED", "status": "running"}""");

        var result = await Undo(handler, waitLimit: TimeSpan.FromMilliseconds(30));

        Assert.Contains("action: pending", result);
        Assert.Contains("task: t1", result);
        Assert.Contains("Do not call undo_change for it again", result);
        Assert.Contains("\"Undo transaction 5: Add Note\"", result);
    }

    [Fact]
    public async Task Undo_RefusesWhenTheTaskFoundAChangedRecord()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.Accepted, """{"task": {"href": "/api/tasks/t1", "id": "t1"}}""");
        handler.Get("/api/tasks/t1", """{"state": "FAILURE", "result": "Object has changed", "status": "failed"}""");

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
    }

    [Fact]
    public async Task Undo_ReportsAFailedTask()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.Accepted, """{"task": {"href": "/api/tasks/t1", "id": "t1"}}""");
        handler.Get("/api/tasks/t1", """{"state": "FAILURE", "result": "Gramps ID missing"}""");

        var ex = await Assert.ThrowsAsync<McpException>(() => Undo(handler));

        Assert.Contains("Gramps ID missing", ex.Message);
        Assert.Contains("Nothing was changed", ex.Message);
    }

    [Fact]
    public async Task Undo_OfADeletion_NeedsForceBeforeGrampsWeb323()
    {
        using var handler = DeleteNoteServer("3.22.1");

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("conflicts: 0", result);
        Assert.Contains("   Restore the deleted Note N0001: [", result);
        Assert.Contains("] Born in Tver [handle: n1]", result);
        Assert.Contains("Gramps Web 3.22.1 restores deleted records only with force=true", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_OfADeletion_RestoresTheRecordOnGrampsWeb323()
    {
        using var handler = DeleteNoteServer("3.23.0");
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id",
            """[{"id": 6, "description": "Undo transaction 5: Delete Note"}]""");

        var result = await Undo(handler);

        Assert.Contains("action: undone", result);
        Assert.Contains("   Restored the deleted Note N0001: [", result);
        Assert.Contains("] Born in Tver [handle: n1]", result);
    }

    [Fact]
    public async Task DryRun_OfADeletion_NamesADeletedPersonWithTypedAttributes()
    {
        var handler = new UndoServer();
        handler.Get($"{HistoryPath}?old=true",
            """
            {
              "id": 5, "description": "Delete Person", "undo": false, "timestamp": 1759242125.5,
              "changes": [
                {"id": 1, "obj_class": "Person", "trans_type": 2, "obj_handle": "p1", "ref_handle": null,
                 "old_data": {"_class": "Person", "handle": "p1", "gramps_id": "I0007", "gender": 0,
                              "primary_name": {"_class": "Name", "first_name": "Maria",
                                               "type": {"_class": "NameType", "value": 2, "string": ""},
                                               "surname_list": [{"_class": "Surname", "surname": "Petrova",
                                                                 "origintype": {"_class": "NameOriginType", "value": 1, "string": ""}}]},
                              "attribute_list": [{"_class": "Attribute", "value": "MH:I7",
                                                  "type": {"_class": "AttributeType", "value": 0, "string": "RIN"}}],
                              "urls": [{"_class": "Url", "path": "https://example.org",
                                        "type": {"_class": "UrlType", "value": 2, "string": ""}}],
                              "event_ref_list": [], "family_list": [], "parent_family_list": [], "tag_list": []}}
              ]
            }
            """);
        handler.Get(CheckPath, NoConflicts);
        handler.Get("/api/metadata/", """{"gramps_webapi": {"version": "3.23.0"}}""");

        var result = await Undo(handler, dryRun: true);

        Assert.Contains("   Restore the deleted Person I0007: Maria Petrova [handle: p1]", result);
    }

    [Fact]
    public async Task Undo_OfADeletion_ExplainsAnOldServerRefusal()
    {
        using var handler = DeleteNoteServer(version: null);
        handler.Post(CheckPath, HttpStatusCode.InternalServerError, """{"error": {"code": 500, "message": "Object has changed"}}""");

        var result = await Undo(handler);

        Assert.Contains("action: refused", result);
        Assert.Contains("Gramps Web before 3.23 refuses this", result);
    }

    [Fact]
    public async Task Undo_ReportsAMissingTransaction()
    {
        using var handler = new UndoServer();

        var result = await Undo(handler, transactionId: 99);

        Assert.StartsWith("Transaction not found: 99", result);
    }

    [Fact]
    public async Task Undo_OfOnlyReferenceChanges_ChangesNothing()
    {
        using var handler = new UndoServer();
        handler.Get($"{HistoryPath}?old=true",
            """{"id": 5, "description": "Link", "changes": [{"obj_class": "7", "trans_type": 0, "obj_handle": "p1", "ref_handle": "n1"}]}""");

        var result = await Undo(handler);

        Assert.Contains("action: unchanged", result);
        Assert.Contains("nothing to undo", result);
    }

    [Fact]
    public async Task Undo_RejectsANonPositiveTransaction()
    {
        using var handler = new UndoServer();

        await Assert.ThrowsAsync<McpException>(() => Undo(handler, transactionId: 0));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Undo_IsRefusedOnAReadOnlyServer_ButDryRunReads()
    {
        using var handler = AddNoteEditPersonServer();
        var client = CreateClient(handler, readOnly: true);

        await Assert.ThrowsAsync<McpException>(() =>
            UndoTools.UndoChangeAsync(5, dryRun: false, force: false, client, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Empty(handler.Requests);

        var result = await UndoTools.UndoChangeAsync(5, dryRun: true, force: false, client, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Contains("action: checked", result);
    }

    [Fact]
    public async Task Undo_AsksTheUser_ThenUndoes()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        handler.Get("/api/transactions/history/?page=1&pagesize=10&sort=-id",
            """[{"id": 6, "description": "Undo transaction 5: Add Note"}]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Undo(handler, user: user);

        var question = Assert.Single(user.Questions).ReplaceLineEndings("\n");
        Assert.StartsWith("Undo transaction 5 (", question);
        Assert.Contains("?\n\nIt will:\n  • Delete Note: [General] A new note\n", question);
        Assert.Contains("  • Restore the earlier version of Person [handle: p1]\n", question);
        Assert.EndsWith("\n\nThe undo is a new transaction, which undo_change can revert in turn.", question);
        Assert.Single(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
        Assert.Contains("action: undone", result);
    }

    [Theory]
    [InlineData("Declined", "declined")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("Unavailable", "not_confirmed")]
    [InlineData("Failed", "not_confirmed")]
    public async Task Undo_WithoutConfirmation_ChangesNothing(string answer, string action)
    {
        using var handler = AddNoteEditPersonServer();

        var result = await Undo(handler, user: new FakeConfirmation(Enum.Parse<ConfirmationAnswer>(answer)));

        Assert.Contains($"action: {action}", result);
        Assert.Contains("nothing was changed", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_DoesNotAsk_ForADryRunOrARefusal()
    {
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);
        using var conflicting = AddNoteEditPersonServer(eventExists: false);

        Assert.Contains("action: checked", await Undo(conflicting, dryRun: true, user: user));
        Assert.Contains("action: refused", await Undo(conflicting, user: user));

        Assert.Empty(user.Questions);
        Assert.DoesNotContain(conflicting.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_WithForce_ShowsTheConflictsWithoutHandles()
    {
        using var handler = AddNoteEditPersonServer(eventExists: false);
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        await Undo(handler, force: true, user: user);

        var question = Assert.Single(user.Questions);
        Assert.Contains("Conflicts it will override (1):", question);
        Assert.Contains("  • The version of Person to restore links Event, which no longer exist", question);
        Assert.Contains("Overriding overwrites later changes", question);
    }

    [Fact]
    public async Task Undo_RefusesWhenTheConflictsChangeWhileTheUserDecides()
    {
        using var handler = AddNoteEditPersonServer();
        handler.Post(CheckPath, HttpStatusCode.OK, "[]");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed, whileDeciding: () => handler.Get(CheckPath,
            """
            {"transaction_id": 5, "can_undo_without_force": false, "total_changes": 2, "conflicts_count": 1,
             "conflicts": [{"change_index": 0, "object_class": "Note", "handle": "n1", "conflict_type": "object_changed",
                            "description": "Object has been modified since this transaction"}]}
            """));

        var result = await Undo(handler, force: true, user: user);

        Assert.Contains("action: refused", result);
        Assert.Contains("changed while the user was deciding", result);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("3.22.1", "3.22")]
    [InlineData("3.23.0rc1", "3.23")]
    [InlineData("v4.0", "4.0")]
    [InlineData("3", null)]
    [InlineData("dev", null)]
    public void ParseVersion_ReadsMajorAndMinor(string version, string? expected)
    {
        Assert.Equal(expected is null ? null : Version.Parse(expected), UndoTools.ParseVersion(version));
    }

    [Fact]
    public void LinkTargets_AreReadFromEveryLinkingKey()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "_class": "Family", "handle": "f1", "father_handle": "p1", "mother_handle": null,
              "child_ref_list": [{"_class": "ChildRef", "ref": "p2", "citation_list": ["c1"], "note_list": []}],
              "event_ref_list": [{"_class": "EventRef", "ref": "e1", "note_list": ["n1"],
                                  "attribute_list": [{"_class": "Attribute", "citation_list": ["c2"]}]}],
              "media_list": [{"_class": "MediaRef", "ref": "m1", "rect": [0, 0, 50, 50]}],
              "lds_ord_list": [{"_class": "LdsOrd", "place": "pl1", "famc": "f2"}],
              "tag_list": ["t1"], "note_list": [], "citation_list": ["c1"]
            }
            """);

        var targets = UndoLinkTargets.Collect(doc.RootElement);

        Assert.Equal(
            new[]
            {
                ("citations", "c1"), ("citations", "c2"), ("events", "e1"), ("families", "f2"), ("media", "m1"),
                ("notes", "n1"), ("people", "p1"), ("people", "p2"), ("places", "pl1"), ("tags", "t1")
            },
            targets.Order());
    }

    private static Task<string> Undo(UndoServer handler, int transactionId = 5, bool dryRun = false, bool force = false,
        TimeSpan? waitLimit = null, IUserConfirmation? user = null) =>
        UndoTools.UndoChangeAsync(transactionId, dryRun, force, CreateClient(handler),
            TimeSpan.FromMilliseconds(1), waitLimit ?? TimeSpan.FromSeconds(5), user);

    /// <summary>Transaction 5 of <see cref="AddNoteEditPerson"/> with the reads its checks and labels make.</summary>
    private static UndoServer AddNoteEditPersonServer(string check = NoConflicts, string noteBacklinks = "{}",
        bool eventExists = true)
    {
        var handler = new UndoServer();
        handler.Get($"{HistoryPath}?old=true", AddNoteEditPerson);
        handler.Get(CheckPath, check);
        handler.Get("/api/notes/n1?keys=handle,backlinks&backlinks=true", $$"""{"handle": "n1", "backlinks": {{noteBacklinks}}}""");
        if (eventExists)
            handler.Get("/api/events/e1?keys=handle", """{"handle": "e1"}""");
        handler.Get("/api/notes/n1", """{"handle": "n1", "gramps_id": "N0001", "text": "A new note", "type": "General"}""");
        return handler;
    }

    private static UndoServer DeleteNoteServer(string? version)
    {
        var handler = new UndoServer();
        handler.Get($"{HistoryPath}?old=true", DeleteNote);
        handler.Get(CheckPath, NoConflicts);
        if (version is not null)
            handler.Get("/api/metadata/", $$$"""{"gramps_webapi": {"version": "{{{version}}}"}}""");
        return handler;
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler, bool readOnly = false)
    {
        var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree", ReadOnly: readOnly);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers the routes a test sets, by method and path with its query, and 404 to any other request.
    /// Records every request other than the token's as "METHOD path?query" with the query unescaped.
    /// </summary>
    private sealed class UndoServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> _routes = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public void Get(string path, string body) => _routes[$"GET {path}"] = new([(HttpStatusCode.OK, body)]);

        /// <summary>Answers each body in turn, then repeats the last.</summary>
        public void GetSequence(string path, params string[] bodies) =>
            _routes[$"GET {path}"] = new(bodies.Select(body => (HttpStatusCode.OK, body)));

        public void Post(string path, HttpStatusCode status, string body) => _routes[$"POST {path}"] = new([(status, body)]);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            var line = $"{request.Method} {Uri.UnescapeDataString(path)}";
            lock (Requests)
                Requests.Add(line);
            var key = request.Method == HttpMethod.Post ? $"POST {request.RequestUri.AbsolutePath}" : $"GET {path}";
            lock (_routes)
            {
                if (!_routes.TryGetValue(key, out var replies))
                    return Task.FromResult(Json(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}"""));
                var (status, body) = replies.Count > 1 ? replies.Dequeue() : replies.Peek();
                return Task.FromResult(Json(status, body));
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
