using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>delete_object asks the user through the client before it deletes.</summary>
public class DeleteConfirmationTests
{
    private const string NotePath = "/api/notes/n1?backlinks=true";

    [Fact]
    public async Task Delete_AsksTheUser_ThenDeletes()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user);

        var question = Assert.Single(user.Questions);
        Assert.Equal(
            "Delete Note N0001: [General] A new note?\n\nThe change history keeps it: undo_change can restore it later.",
            question.ReplaceLineEndings("\n"));
        Assert.Contains("action: deleted", result);
        Assert.Contains("DELETE /api/notes/n1", server.Requests);
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

        Assert.StartsWith($"---\ntype: Note\naction: {action}\nhandle: n1\n---\n", result);
        Assert.Contains(reason, result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_WithForce_NamesTheRecordsWhoseLinkGoes()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1"]}""");
        server.Get("/api/people/p1?profile=self",
            """{"handle": "p1", "gramps_id": "I0001", "profile": {"handle": "p1", "name_display": "Smith, Anna"}}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user, force: true);

        var question = Assert.Single(user.Questions).ReplaceLineEndings("\n");
        Assert.Contains("1 record links to it; Gramps Web removes that link:\n  • Person: Smith, Anna\n", question);
        Assert.EndsWith("The change history keeps all of this: undo_change can restore it later.", question);
        Assert.Contains("action: deleted", result);
    }

    [Fact]
    public async Task Delete_WithForce_NamesTheFirstLinkingRecords_AndCountsTheRest()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1", "p2", "p3", "p4", "p5", "p6", "p7"]}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        await Delete(server, user, force: true);

        var question = Assert.Single(user.Questions);
        Assert.Contains("7 records link to it; Gramps Web removes those links:", question);
        Assert.Contains("  • Person [handle: p5]", question);
        Assert.DoesNotContain("p6", question);
        Assert.Contains("  • … and 2 more", question);
    }

    [Fact]
    public async Task Delete_RefusesBacklinksWithoutForce_BeforeAsking()
    {
        using var server = NoteServer(backlinks: """{"person": ["p1"]}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Delete(server, user);

        Assert.Empty(user.Questions);
        Assert.StartsWith("Cannot delete Note [n1]: other objects reference it.", result);
        Assert.EndsWith("or pass force=true once the user confirmed deleting it anyway. " +
                        "Gramps Web then removes their links to it.", result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_OfAPlace_SaysThePlacesInsideItKeepTheirLink()
    {
        using var server = new RouteServer();
        server.Get("/api/places/pl1?backlinks=true",
            """{"handle": "pl1", "gramps_id": "P0001", "change": 1, "backlinks": {"event": ["e1"], "place": ["pl2", "pl3"]}}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        var refusal = await ObjectTools.DeleteObjectAsync("place", "pl1", force: false, CreateClient(server), user);
        await ObjectTools.DeleteObjectAsync("place", "pl1", force: true, CreateClient(server), user);

        Assert.EndsWith("Gramps Web then removes their links to it. The places inside it keep a link to the missing " +
                        "place, so change their enclosedBy with update_place first.", refusal);
        var question = Assert.Single(user.Questions).ReplaceLineEndings("\n");
        Assert.Contains("1 record links to it; Gramps Web removes that link:\n  • Event [handle: e1]\n", question);
        Assert.Contains("2 places inside it keep links to the missing place:\n" +
                        "  • Place [handle: pl2]\n  • Place [handle: pl3]\n", question);
    }

    [Fact]
    public async Task Delete_OfASource_SaysItsCitationsGoWithIt()
    {
        using var server = new RouteServer();
        server.Get("/api/sources/s1?backlinks=true",
            """{"handle": "s1", "gramps_id": "S0001", "change": 1, "backlinks": {"citation": ["c1", "c2"]}}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        var refusal = await ObjectTools.DeleteObjectAsync("source", "s1", force: false, CreateClient(server), user);
        await ObjectTools.DeleteObjectAsync("source", "s1", force: true, CreateClient(server), user);

        Assert.EndsWith("Gramps Web then deletes its citations and removes the links to them.", refusal);
        Assert.Contains("Gramps Web also deletes its 2 citations and removes the links to them:\n" +
                        "  • Citation [handle: c1]\n  • Citation [handle: c2]\n",
            Assert.Single(user.Questions).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Delete_OfAPerson_SaysAnEmptyFamilyGoesWithThem()
    {
        using var server = new RouteServer();
        server.Get("/api/people/p1?backlinks=true",
            """{"handle": "p1", "gramps_id": "I0001", "change": 1, "backlinks": {"family": ["f1"]}}""");
        var user = new FakeConfirmation(ConfirmationAnswer.Declined);

        var refusal = await ObjectTools.DeleteObjectAsync("person", "p1", force: false, CreateClient(server), user);
        await ObjectTools.DeleteObjectAsync("person", "p1", force: true, CreateClient(server), user);

        Assert.EndsWith("Gramps Web then removes their links to it. A family left without father, mother and " +
                        "children is deleted with it.", refusal);
        Assert.Contains("  • Family [handle: f1]\nA family left without father, mother and children is deleted with it.\n",
            Assert.Single(user.Questions).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Delete_RefusesARecordLinkedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed,
            whileDeciding: () => server.Get(NotePath, Note(backlinks: """{"person": ["p1"]}""")));

        var result = await Delete(server, user);

        Assert.Contains("action: refused", result);
        Assert.Contains("changed while the user was deciding, so nothing was deleted", result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_RefusesARecordEditedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed,
            whileDeciding: () => server.Get(NotePath, Note(change: 1759242200)));

        var result = await Delete(server, user);

        Assert.Contains("action: refused", result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_ReportsARecordDeletedWhileTheUserDecides()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed, whileDeciding: () => server.Remove(NotePath));

        var result = await Delete(server, user);

        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_OnAReadOnlyServer_FailsBeforeAsking()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        await Assert.ThrowsAsync<McpException>(() =>
            ObjectTools.DeleteObjectAsync("note", "n1", force: false, CreateClient(server, readOnly: true), user));

        Assert.Empty(user.Questions);
    }

    [Fact]
    public async Task Delete_WithoutAnAskingClient_DeletesAsBefore()
    {
        using var server = NoteServer();

        var result = await ObjectTools.DeleteObjectAsync("note", "n1", force: false, CreateClient(server),
            UserConfirmation.For(null, "delete_object"));

        Assert.Contains("action: deleted", result);
    }

    private static Task<string> Delete(RouteServer server, IUserConfirmation user, bool force = false) =>
        ObjectTools.DeleteObjectAsync("note", "n1", force, CreateClient(server), user);

    private static string Note(string backlinks = "{}", long change = 1759242125) =>
        $$"""{"handle": "n1", "gramps_id": "N0001", "change": {{change}}, "backlinks": {{backlinks}}}""";

    /// <summary>Note N0001 with the reads the delete and its question make, and its DELETE.</summary>
    private static RouteServer NoteServer(string backlinks = "{}")
    {
        var server = new RouteServer();
        server.Get(NotePath, Note(backlinks));
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
    /// Answers the GET routes a test sets and every DELETE, and 404 to any other request.
    /// Records every request other than the token's as "METHOD path?query".
    /// </summary>
    private sealed class RouteServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            lock (Requests)
                Requests.Add($"{request.Method} {Uri.UnescapeDataString(path)}");
            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            lock (_routes)
            {
                return Task.FromResult(request.Method == HttpMethod.Get && _routes.TryGetValue(path, out var body)
                    ? Json(HttpStatusCode.OK, body)
                    : Json(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}"""));
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
