using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// delete_records checks the records together, asks the user through the client, and deletes each type in its
/// own delete-by-handle transaction.
/// </summary>
[Collection("HandleCache")]
public class DeleteRecordsTests
{
    private const string Probe = "keys=handle,gramps_id,change,backlinks&backlinks=true";
    private const string DeletePath = "/api/objects/delete-by-handle/";

    public DeleteRecordsTests() => HandleCache.Invalidate();

    [Fact]
    public async Task Delete_AsksTheUser_ThenDeletesInOneTransaction()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user);

        var question = Assert.Single(user.Questions);
        Assert.Equal(
            "Delete Note N0001: [General] A new note?\n\nThe change history keeps it: undo_change can restore it later.",
            question.ReplaceLineEndings("\n"));
        Assert.Equal("""{"namespace":"notes","handles":["n1"]}""", Assert.Single(server.Deletes));
        Assert.StartsWith("---\ntype: Transaction\naction: deleted\nrecords: 1\n---\n", result);
        Assert.Contains("Deleted 1 record in one transaction", result);
        Assert.Contains("Note: [General] A new note — handle: n1 | gramps_id: N0001", result);
        Assert.DoesNotContain("Also deleted", result);
    }

    [Theory]
    [InlineData("Declined", "declined", "Do not call again unless the user asks again")]
    [InlineData("Cancelled", "cancelled", "Ask the user in chat before calling again")]
    [InlineData("Unavailable", "not_confirmed", "GRAMPS_CONFIRMATION=required")]
    [InlineData("Failed", "not_confirmed", "could not show the confirmation dialog")]
    public async Task Delete_WithoutConfirmation_DeletesNothing(string answer, string action, string reason)
    {
        using var server = NoteServer();

        var result = await Delete(server, new FakeConfirmation(Enum.Parse<ConfirmationAnswer>(answer)));

        Assert.StartsWith($"---\ntype: Transaction\naction: {action}\nrecords: 1\n---\n", result);
        Assert.Contains(reason, result);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_WithForce_NamesTheRecordsWhoseLinksGo_AndReportsThem()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1"]}""");
        server.Get("/api/people/p1?profile=self",
            """{"handle": "p1", "gramps_id": "I0001", "profile": {"handle": "p1", "name_display": "Smith, Anna"}}""");
        server.DeleteReply = _ => (HttpStatusCode.OK, """
            [{"_class": "Person", "handle": "p1", "type": "update", "old": {"handle": "p1", "gramps_id": "I0001"},
              "new": {"handle": "p1", "gramps_id": "I0001"}},
             {"_class": "Note", "handle": "n1", "type": "delete", "old": {"handle": "n1", "gramps_id": "N0001"}, "new": null}]
            """);
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user, force: true);

        var question = Assert.Single(user.Questions).ReplaceLineEndings("\n");
        Assert.Contains("1 other record links to it; Gramps Web removes that link:\n  • Person: Smith, Anna\n", question);
        Assert.Contains("action: deleted", result);
        Assert.Contains("Links to them removed from (1):\nPerson", result.ReplaceLineEndings("\n"));
        Assert.Contains("— handle: p1 | gramps_id: I0001", result);
    }

    [Fact]
    public async Task Delete_WithForce_NamesTheFirstLinkingRecords_AndCountsTheRest()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1", "p2", "p3", "p4", "p5", "p6", "p7"]}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        await Delete(server, user, force: true);

        var question = Assert.Single(user.Questions);
        Assert.Contains("7 other records link to it; Gramps Web removes those links:", question);
        Assert.Contains("  • Person [handle: p5]", question);
        Assert.DoesNotContain("p6", question);
        Assert.Contains("  • … and 2 more", question);
    }

    [Fact]
    public async Task Delete_RefusesOutsideLinksWithoutForce_BeforeAsking()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1"], "event": ["e1", "e2"]}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user);

        Assert.Empty(user.Questions);
        Assert.StartsWith("---\ntype: Transaction\naction: refused\nrecords: 1\n---\n" +
                          "Nothing was deleted: other records link to it.\n", result.ReplaceLineEndings("\n"));
        Assert.Contains("  • Note N0001: linked from people: 1, events: 2", result);
        Assert.Contains("force=true", result);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_LinksBetweenTheRecordsOfTheCall_NeedNoForce_AndFamiliesGoFirst()
    {
        using var server = new RouteServer();
        server.Get($"/api/people/?handles=p1,p2&{Probe}&page=1&pagesize=2", $"""
            [{ProbeJson("p1", "I0001", """{"family": ["f1"]}""")}, {ProbeJson("p2", "I0002", """{"family": ["f1"]}""")}]
            """);
        server.Get($"/api/families/f1?{Probe}", ProbeJson("f1", "F0001"));
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        server.Get("/api/people/?gramps_id=I0001&pagesize=1", """[{"handle": "p1"}]""");
        server.Get("/api/people/?gramps_id=I0002&pagesize=1", """[{"handle": "p2"}]""");
        server.Get("/api/families/?gramps_id=F0001&pagesize=1", """[{"handle": "f1"}]""");

        var result = await ObjectTools.DeleteRecordsAsync(["I0001", "F0001", "I0002"], null, false, CreateClient(server), user);

        Assert.Equal(
            "Delete 3 records?\n  • Person I0001\n  • Family F0001\n  • Person I0002\n\n" +
            "The change history keeps them: undo_change can restore them later.",
            Assert.Single(user.Questions).ReplaceLineEndings("\n"));
        Assert.Equal(
            ["""{"namespace":"families","handles":["f1"]}""", """{"namespace":"people","handles":["p1","p2"]}"""],
            server.Deletes);
        Assert.Contains("records: 3", result);
        Assert.Contains("Deleted 3 records in 2 transactions, one per record type", result);
    }

    [Fact]
    public async Task Delete_GivenTheSameRecordTwice_DeletesItOnce()
    {
        using var server = NoteServer();
        server.Get("/api/notes/?gramps_id=N0001&pagesize=1", """[{"handle": "n1"}]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await ObjectTools.DeleteRecordsAsync(["N0001", "n1"], "note", false, CreateClient(server), user);

        Assert.StartsWith("Delete Note N0001", Assert.Single(user.Questions));
        Assert.Equal("""{"namespace":"notes","handles":["n1"]}""", Assert.Single(server.Deletes));
        Assert.Contains("records: 1", result);
    }

    [Fact]
    public async Task Delete_WithAMissingRecord_DeletesNothing()
    {
        using var server = new RouteServer();
        server.Get($"/api/notes/?handles=n1,n2&{Probe}&page=1&pagesize=2", $"[{ProbeJson("n1", "N0001")}]");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await ObjectTools.DeleteRecordsAsync(["n1", "n2"], "note", false, CreateClient(server), user);

        Assert.StartsWith("Nothing was deleted: 1 of 2 records were not found.", result);
        Assert.Contains("n2", result);
        Assert.Empty(user.Questions);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_OfASource_CountsItsCitationsAsDeletedAlong()
    {
        using var server = new RouteServer();
        server.Get($"/api/sources/s1?{Probe}", ProbeJson("s1", "S0001", """{"citation": ["c1", "c2"], "repository": []}"""));
        server.DeleteReply = _ => (HttpStatusCode.OK, """
            [{"_class": "Citation", "handle": "c1", "type": "delete", "old": {"handle": "c1", "gramps_id": "C0001"}, "new": null},
             {"_class": "Citation", "handle": "c2", "type": "delete", "old": {"handle": "c2", "gramps_id": "C0002"}, "new": null},
             {"_class": "Source", "handle": "s1", "type": "delete", "old": {"handle": "s1", "gramps_id": "S0001"}, "new": null}]
            """);
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var refused = await ObjectTools.DeleteRecordsAsync(["s1"], "source", false, CreateClient(server), user);
        Assert.Contains("linked from citations: 2", refused);
        Assert.Contains("Gramps Web also deletes 2 citations that cite a deleted source.", refused);

        var result = await ObjectTools.DeleteRecordsAsync(["s1"], "source", true, CreateClient(server), user);

        var question = Assert.Single(user.Questions);
        Assert.DoesNotContain("other record", question);
        Assert.Contains("Gramps Web also deletes 2 citations that cite a deleted source.", question);
        Assert.Contains("Also deleted by Gramps Web (2):", result);
        Assert.Contains("handle: c2 | gramps_id: C0002", result);
    }

    [Fact]
    public async Task Delete_OfAPlaceThatEnclosesOthers_IsRefusedEvenWithForce()
    {
        using var server = new RouteServer();
        server.Get($"/api/places/pl1?{Probe}", ProbeJson("pl1", "P0001", """{"place": ["pl2"], "event": ["e1"]}"""));
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await ObjectTools.DeleteRecordsAsync(["pl1"], "place", true, CreateClient(server), user);

        Assert.Contains("action: refused", result);
        Assert.Contains("  • Place P0001 encloses:\n      Place — handle: pl2", result.ReplaceLineEndings("\n"));
        Assert.Contains("update_place", result);
        Assert.Empty(user.Questions);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_OfAPlaceWithTheEnclosedPlaces_Deletes()
    {
        using var server = new RouteServer();
        server.Get($"/api/places/?handles=pl1,pl2&{Probe}&page=1&pagesize=2",
            $"""[{ProbeJson("pl1", "P0001", """{"place": ["pl2"]}""")}, {ProbeJson("pl2", "P0002")}]""");

        var result = await ObjectTools.DeleteRecordsAsync(["pl1", "pl2"], "place", false, CreateClient(server),
            new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.Contains("action: deleted", result);
        Assert.Equal("""{"namespace":"places","handles":["pl1","pl2"]}""", Assert.Single(server.Deletes));
    }

    [Fact]
    public async Task Delete_WhenALaterTypeFails_ReportsWhatWasDeleted()
    {
        using var server = NoteServer();
        server.Get($"/api/people/p1?{Probe}", ProbeJson("p1", "I0001"));
        server.DeleteReply = body => body.Contains("\"notes\"")
            ? (HttpStatusCode.InternalServerError, """{"error": {"code": 500, "message": "Database is locked"}}""")
            : (HttpStatusCode.OK, """[{"_class": "Person", "handle": "p1", "type": "delete", "old": {}, "new": null}]""");

        server.Get("/api/people/?gramps_id=I0001&pagesize=1", """[{"handle": "p1"}]""");

        var result = await ObjectTools.DeleteRecordsAsync(["I0001", "N0001"], null, false, CreateClient(server),
            new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.StartsWith("---\ntype: Transaction\naction: partially_deleted\nrecords: 1\n---\n", result);
        Assert.Contains("Deleted 1 of 2 records; deleting the notes failed", result);
        Assert.Contains("Deleted:\nPerson — handle: p1 | gramps_id: I0001", result.ReplaceLineEndings("\n"));
        Assert.Contains("Not deleted:\nNote: [General] A new note — handle: n1 | gramps_id: N0001",
            result.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Delete_WhenTheFirstTypeFails_Throws()
    {
        using var server = NoteServer();
        server.DeleteReply = _ => (HttpStatusCode.InternalServerError, """{"error": {"code": 500, "message": "Database is locked"}}""");

        await Assert.ThrowsAsync<McpException>(() => Delete(server, new FakeConfirmation(ConfirmationAnswer.Confirmed)));
    }

    [Fact]
    public async Task Delete_RefusesARecordLinkedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed,
            whileDeciding: () => server.Get(NoteProbePath, ProbeJson("n1", "N0001", """{"person": ["p1"]}""")));

        var result = await Delete(server, user, force: true);

        Assert.Contains("action: refused", result);
        Assert.Contains("changed while the user was deciding, so nothing was deleted", result);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_RefusesARecordEditedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed,
            whileDeciding: () => server.Get(NoteProbePath, ProbeJson("n1", "N0001", change: 1759242200)));

        var result = await Delete(server, user);

        Assert.Contains("action: refused", result);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_RefusesARecordDeletedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed, whileDeciding: () => server.Remove(NoteProbePath));

        var result = await Delete(server, user);

        Assert.Contains("action: refused", result);
        Assert.Empty(server.Deletes);
    }

    [Fact]
    public async Task Delete_OnAReadOnlyServer_FailsBeforeAsking()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        await Assert.ThrowsAsync<McpException>(() =>
            ObjectTools.DeleteRecordsAsync(["n1"], "note", false, CreateClient(server, readOnly: true), user));

        Assert.Empty(user.Questions);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Delete_WithoutAnAskingClient_Deletes()
    {
        using var server = NoteServer();

        var result = await ObjectTools.DeleteRecordsAsync(["n1"], "note", false, CreateClient(server),
            UserConfirmation.For(null, "delete_records"));

        Assert.Contains("action: deleted", result);
        Assert.Single(server.Deletes);
    }

    private const string NoteProbePath = $"/api/notes/n1?{Probe}";

    private static Task<string> Delete(RouteServer server, IUserConfirmation user, bool force = false) =>
        ObjectTools.DeleteRecordsAsync(["n1"], "note", force, CreateClient(server), user);

    private static string ProbeJson(string handle, string grampsId, string backlinks = "{}", long change = 1759242125) =>
        $$"""{"handle": "{{handle}}", "gramps_id": "{{grampsId}}", "change": {{change}}, "backlinks": {{backlinks}}}""";

    /// <summary>Note N0001 with the reads the delete and its question make.</summary>
    private static RouteServer NoteServer(string backlinks = "{}")
    {
        var server = new RouteServer();
        server.Get(NoteProbePath, ProbeJson("n1", "N0001", backlinks));
        server.Get("/api/notes/?gramps_id=N0001&pagesize=1", """[{"handle": "n1"}]""");
        server.Get("/api/notes/n1", """{"handle": "n1", "gramps_id": "N0001", "text": "A new note", "type": "General"}""");
        return server;
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler, bool readOnly = false)
    {
        var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree", ReadOnly: readOnly);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers the GET routes a test sets and the delete-by-handle POST, and 404 to any other request. Records
    /// every request other than the token's as "METHOD path?query", and the bodies of the deletes.
    /// </summary>
    private sealed class RouteServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public List<string> Deletes { get; } = [];

        /// <summary>The reply to a delete by its body; by default every handle deleted.</summary>
        public Func<string, (HttpStatusCode Status, string Body)> DeleteReply { get; set; } = DeletedAll;

        public void Get(string path, string body)
        {
            lock (_routes)
                _routes[path] = body;
        }

        public void Remove(string path)
        {
            lock (_routes)
                _routes.Remove(path);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            lock (Requests)
                Requests.Add($"{request.Method} {Uri.UnescapeDataString(path)}");
            if (request.Method == HttpMethod.Post && path == DeletePath)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                lock (Deletes)
                    Deletes.Add(body);
                var (status, reply) = DeleteReply(body);
                return Json(status, reply);
            }

            lock (_routes)
            {
                return request.Method == HttpMethod.Get && _routes.TryGetValue(path, out var found)
                    ? Json(HttpStatusCode.OK, found)
                    : Json(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}""");
            }
        }

        private static (HttpStatusCode, string) DeletedAll(string body)
        {
            var request = JsonNode.Parse(body)!;
            var collection = request["namespace"]!.GetValue<string>();
            var changes = new JsonArray(request["handles"]!.AsArray()
                .Select(h => (JsonNode)new JsonObject
                {
                    ["_class"] = DeleteHelper.ClassOf(collection),
                    ["handle"] = h!.GetValue<string>(),
                    ["type"] = "delete",
                    ["old"] = new JsonObject { ["handle"] = h.GetValue<string>() },
                    ["new"] = null
                }).ToArray());
            return (HttpStatusCode.OK, changes.ToJsonString());
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
