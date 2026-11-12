using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>merge_objects asks the user, then has Gramps Web merge the duplicate into the record to keep.</summary>
public class MergeObjectsTests
{
    private const string NoteMerge = "POST /api/notes/n1/merge/n2";
    private const string HistoryPath = "/api/transactions/history/?page=1&pagesize=10&sort=-id";

    [Fact]
    public async Task Merge_AsksTheUser_ThenMerges()
    {
        using var server = NoteServer();
        server.Get("/api/notes/?handles=n1,n2&page=1&pagesize=2",
            """[{"handle": "n1", "gramps_id": "N0001", "type": "General", "text": {"string": "Born in Tver"}},""" +
            """ {"handle": "n2", "gramps_id": "N0002", "type": "General", "text": {"string": "Born at Tver"}}]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = (await Merge(server, user)).ReplaceLineEndings("\n");

        var question = Assert.Single(user.Questions).ReplaceLineEndings("\n");
        Assert.StartsWith(
            "Merge Note N0002 into N0001?\n\n  • Keep N0001: [General] Born in Tver\n" +
            "  • Merge into it and delete N0002: [General] Born at Tver\n\nThe kept note keeps its text and type",
            question);
        Assert.EndsWith("\n\nThe change history keeps the duplicate: undo_change can undo the merge later.", question);
        Assert.Contains(NoteMerge, server.Requests);
        Assert.Equal("{}", server.Bodies[NoteMerge]);
        Assert.StartsWith(
            "---\ntype: Note\naction: merged\nhandle: n1\ngramps_id: N0001\nduplicate_handle: n2\n" +
            "duplicate_gramps_id: N0002\ntransaction: 41\n---\nN0002 was merged into N0001 and deleted",
            result);
        Assert.Contains("undo_change(transactionId: 41)", result);
        Assert.DoesNotContain("also joined", result);
    }

    [Theory]
    [InlineData("Declined", "declined", "Do not call again unless the user asks again")]
    [InlineData("Cancelled", "cancelled", "Ask the user in chat before calling again")]
    [InlineData("Unavailable", "not_confirmed", "GRAMPS_CONFIRMATION=required")]
    [InlineData("Failed", "not_confirmed", "could not show the confirmation dialog")]
    public async Task Merge_WithoutConfirmation_MergesNothing(string answer, string action, string reason)
    {
        using var server = NoteServer();

        var result = await Merge(server, new FakeConfirmation(Enum.Parse<ConfirmationAnswer>(answer)));

        Assert.StartsWith($"---\ntype: Note\naction: {action}\nhandle: n1\nduplicate_handle: n2\n---\n", result);
        Assert.Contains(reason, result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_WhenARecordChangesWhileTheUserDecides_MergesNothing()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed,
            whileDeciding: () => server.Get("/api/notes/n2", Note("n2", "N0002", change: 1700000999)));

        var result = await Merge(server, user);

        Assert.StartsWith("---\ntype: Note\naction: refused\nhandle: n1\nduplicate_handle: n2\n---\n", result);
        Assert.Contains("changed while the user was deciding", result);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_WithoutDialog_MergesWithoutAsking()
    {
        using var server = NoteServer();

        var result = await MergeTools.MergeObjectsAsync("note", "n1", "n2", true, null, null,
            CreateClient(server), UserConfirmation.None);

        Assert.Contains("action: merged", result);
        Assert.Contains(NoteMerge, server.Requests);
    }

    [Fact]
    public async Task Merge_OfPartners_IsRefusedBeforeAsking()
    {
        using var server = PeopleServer(keepFamilies: """["f1"]""", duplicateFamilies: """["f1"]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await MergePeople(server, user);

        Assert.StartsWith("---\ntype: Person\naction: refused\nhandle: p1\nduplicate_handle: p2\n---\n", result);
        Assert.Contains("spouses cannot be merged", result);
        Assert.Contains("Nothing was changed.", result);
        Assert.Empty(user.Questions);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_OfParentAndChild_IsRefusedBeforeAsking()
    {
        using var server = PeopleServer(keepFamilies: """["f1"]""", duplicateParents: """["f1"]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await MergePeople(server, user);

        Assert.Contains("action: refused", result);
        Assert.Contains("a parent and child cannot be merged", result);
        Assert.Empty(user.Questions);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, """{"family_merger":true}""")]
    [InlineData(false, """{"family_merger":false}""")]
    public async Task Merge_OfPeople_SendsTheFamilyChoice(bool mergeFamilies, string body)
    {
        using var server = PeopleServer();
        server.Get(HistoryPath,
            """[{"id": 7, "description": "Merge Person", "changes": [""" +
            """{"obj_class": "Person", "trans_type": 2, "obj_handle": "p2"},""" +
            """{"obj_class": "Family", "trans_type": 2, "obj_handle": "f2"},""" +
            """{"obj_class": "7", "trans_type": 2, "obj_handle": "f3"},""" +
            """{"obj_class": "Person", "trans_type": 1, "obj_handle": "p1"}]}]""");

        var result = await MergePeople(server, new FakeConfirmation(ConfirmationAnswer.Confirmed), mergeFamilies);

        Assert.Equal(body, server.Bodies["POST /api/people/p1/merge/p2"]);
        Assert.Contains("transaction: 7", result);
        Assert.Contains("The same merge also joined and deleted 1 family.", result);
    }

    [Fact]
    public async Task Merge_OfPeopleWithParents_SuggestsMergingTheParentFamilies()
    {
        using var server = PeopleServer(keepParents: """["f1"]""", duplicateParents: """["f2"]""");

        var result = await MergePeople(server, new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.Contains("now has two parent families", result);
        Assert.Contains("merge_objects(objectType: \"family\"", result);
    }

    [Fact]
    public async Task Merge_OfFamilies_KeepsTheKeptFamilysParentsAndMergesTheOthers()
    {
        using var server = FamilyServer(keepFather: "a1", keepMother: "b1", duplicateFather: "a2", duplicateMother: "b1");
        server.Get("/api/people/?handles=a1,a2&profile=self&page=1&pagesize=2",
            """[{"handle": "a1", "gramps_id": "I0001", "profile": {"handle": "a1", "name_display": "Smith, John"}},""" +
            """ {"handle": "a2", "gramps_id": "I0002", "profile": {"handle": "a2", "name_display": "Smith, Johann"}}]""");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        await MergeFamilies(server, user);

        Assert.Equal("""{"phoenix_father_handle":"a1","phoenix_mother_handle":"b1"}""",
            server.Bodies["POST /api/families/k1/merge/d1"]);
        Assert.Contains(
            "The two fathers become one person: Smith, John stays, and Smith, Johann is merged into that person and deleted.",
            Assert.Single(user.Questions));
        Assert.DoesNotContain("mothers become one person", user.Questions[0]);
    }

    [Fact]
    public async Task Merge_OfFamilies_TakesTheDuplicatesParentWhenTheKeptFamilyHasNone()
    {
        using var server = FamilyServer(keepFather: null, keepMother: "b1", duplicateFather: "a2", duplicateMother: null);

        await MergeFamilies(server, new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.Equal("""{"phoenix_father_handle":"a2","phoenix_mother_handle":"b1"}""",
            server.Bodies["POST /api/families/k1/merge/d1"]);
    }

    [Fact]
    public async Task Merge_OfFamilies_KeepsTheChosenFather()
    {
        using var server = FamilyServer(keepFather: "a1", keepMother: null, duplicateFather: "a2", duplicateMother: null);

        await MergeFamilies(server, new FakeConfirmation(ConfirmationAnswer.Confirmed), fatherHandle: "a2");

        Assert.Equal("""{"phoenix_father_handle":"a2"}""", server.Bodies["POST /api/families/k1/merge/d1"]);
    }

    [Fact]
    public async Task Merge_OfFamilies_RejectsAFatherOfNeitherFamily()
    {
        using var server = FamilyServer(keepFather: "a1", keepMother: null, duplicateFather: "a2", duplicateMother: null);
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            MergeFamilies(server, user, fatherHandle: "a3"));

        Assert.Contains("fatherHandle must be the father of one of the two families", ex.Message);
        Assert.Empty(user.Questions);
    }

    [Fact]
    public async Task Merge_RefusedByGramps_ChangesNothing()
    {
        using var server = NoteServer();
        server.Post(NoteMerge, HttpStatusCode.Conflict,
            """{"error": {"code": 409, "message": "Spouses cannot be merged. To merge these people, you must first break the relationship between them."}}""");

        var result = await Merge(server, new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.StartsWith("---\ntype: Note\naction: refused\nhandle: n1\nduplicate_handle: n2\n---\n", result);
        Assert.Contains("Gramps refuses this merge: Spouses cannot be merged.", result);
        Assert.EndsWith("Nothing was changed.", result);
    }

    [Fact]
    public async Task Merge_AnsweredWithABareNotFound_IsAnError()
    {
        using var server = NoteServer();
        server.Post(NoteMerge, HttpStatusCode.NotFound,
            """{"error": {"code": 404, "message": "The requested URL was not found on the server."}}""");

        var ex = await Assert.ThrowsAsync<McpException>(() => Merge(server, new FakeConfirmation(ConfirmationAnswer.Confirmed)));

        Assert.Contains("The requested URL was not found on the server.", ex.Message);
    }

    [Fact]
    public async Task Merge_OfARecordDeletedMeanwhile_IsRefused()
    {
        using var server = NoteServer();
        server.Post(NoteMerge, HttpStatusCode.NotFound,
            """{"error": {"code": 404, "message": "Titanic handle not found"}}""");

        var result = await Merge(server, new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.Contains("action: refused", result);
        Assert.Contains("was deleted meanwhile", result);
    }

    [Fact]
    public async Task Merge_WithoutReadableHistory_PointsToRecentChanges()
    {
        using var server = NoteServer();
        server.Remove(HistoryPath);

        var result = await Merge(server, new FakeConfirmation(ConfirmationAnswer.Confirmed));

        Assert.Contains("action: merged", result);
        Assert.DoesNotContain("transaction:", result);
        Assert.Contains("find it in get_recent_changes", result);
    }

    [Fact]
    public async Task Merge_OfAMissingRecord_SaysItIsNotFound()
    {
        using var server = NoteServer();
        server.Remove("/api/notes/n2");
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var result = await Merge(server, user);

        Assert.Contains("n2", result);
        Assert.Contains("not found", result, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(user.Questions);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tag", "n1", "n2", "Tags cannot be merged")]
    [InlineData("thing", "n1", "n2", "Invalid objectType")]
    [InlineData("note", "n1", "n1", "name the same record")]
    [InlineData("note", "n1", " ", "keepHandle and duplicateHandle are both required")]
    public async Task Merge_WithInvalidArguments_AsksNothing(string objectType, string keep, string duplicate, string error)
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            MergeTools.MergeObjectsAsync(objectType, keep, duplicate, true, null, null, CreateClient(server), user));

        Assert.Contains(error, ex.Message);
        Assert.Empty(user.Questions);
        Assert.DoesNotContain(server.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Merge_WithParentsForAnotherType_IsRejected()
    {
        using var server = NoteServer();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            MergeTools.MergeObjectsAsync("note", "n1", "n2", true, "a1", null, CreateClient(server),
                new FakeConfirmation(ConfirmationAnswer.Confirmed)));

        Assert.Contains("apply only to objectType family", ex.Message);
    }

    [Fact]
    public async Task Merge_InReadOnlyMode_FailsBeforeAsking()
    {
        using var server = NoteServer();
        var user = new FakeConfirmation(ConfirmationAnswer.Confirmed);

        await Assert.ThrowsAsync<McpException>(() =>
            MergeTools.MergeObjectsAsync("note", "n1", "n2", true, null, null, CreateClient(server, readOnly: true), user));

        Assert.Empty(user.Questions);
        Assert.Empty(server.Requests);
    }

    private static Task<string> Merge(RouteServer server, IUserConfirmation user) =>
        MergeTools.MergeObjectsAsync("note", "n1", "n2", true, null, null, CreateClient(server), user);

    private static Task<string> MergePeople(RouteServer server, IUserConfirmation user, bool mergeFamilies = true) =>
        MergeTools.MergeObjectsAsync("person", "p1", "p2", mergeFamilies, null, null, CreateClient(server), user);

    private static Task<string> MergeFamilies(RouteServer server, IUserConfirmation user, string? fatherHandle = null) =>
        MergeTools.MergeObjectsAsync("family", "k1", "d1", true, fatherHandle, null, CreateClient(server), user);

    private static string Note(string handle, string id, long change = 1700000000) =>
        $$"""{"handle": "{{handle}}", "gramps_id": "{{id}}", "change": {{change}}, "type": "General", "text": {"string": "x"} }""";

    private static RouteServer NoteServer()
    {
        var server = new RouteServer();
        server.Get("/api/notes/n1", Note("n1", "N0001"));
        server.Get("/api/notes/n2", Note("n2", "N0002"));
        server.Get(HistoryPath,
            """[{"id": 41, "description": "Merge Notes", "changes": [""" +
            """{"obj_class": "Note", "trans_type": 2, "obj_handle": "n2"},""" +
            """{"obj_class": "Note", "trans_type": 1, "obj_handle": "n1"}]},""" +
            """{"id": 40, "description": "Delete Note", "changes": [{"obj_class": "Note", "trans_type": 2, "obj_handle": "n9"}]}]""");
        return server;
    }

    private static RouteServer PeopleServer(string keepFamilies = "[]", string duplicateFamilies = "[]",
        string keepParents = "[]", string duplicateParents = "[]")
    {
        var server = new RouteServer();
        server.Get("/api/people/p1",
            $$"""{"handle": "p1", "gramps_id": "I0001", "change": 1, "family_list": {{keepFamilies}}, "parent_family_list": {{keepParents}}}""");
        server.Get("/api/people/p2",
            $$"""{"handle": "p2", "gramps_id": "I0002", "change": 2, "family_list": {{duplicateFamilies}}, "parent_family_list": {{duplicateParents}}}""");
        return server;
    }

    private static RouteServer FamilyServer(string? keepFather, string? keepMother, string? duplicateFather,
        string? duplicateMother)
    {
        var server = new RouteServer();
        server.Get("/api/families/k1", Family("k1", "F0001", keepFather, keepMother));
        server.Get("/api/families/d1", Family("d1", "F0002", duplicateFather, duplicateMother));
        return server;

        static string Family(string handle, string id, string? father, string? mother) =>
            $$"""{"handle": "{{handle}}", "gramps_id": "{{id}}", "change": 1, "father_handle": {{Quote(father)}}, "mother_handle": {{Quote(mother)}}}""";

        static string Quote(string? handle) => handle is null ? "null" : $"\"{handle}\"";
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler, bool readOnly = false)
    {
        var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree", ReadOnly: readOnly);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers GETs from its routes and merges with 200 <c>{}</c> unless a POST route says otherwise; records
    /// each request and each POST body.
    /// </summary>
    private sealed class RouteServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _posts = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

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

        public void Post(string request, HttpStatusCode status, string body) => _posts[request] = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Json(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            var line = $"{request.Method} {Uri.UnescapeDataString(path)}";
            lock (Requests)
                Requests.Add(line);
            if (request.Method == HttpMethod.Post)
            {
                Bodies[line] = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                return _posts.TryGetValue(line, out var reply) ? Json(reply.Status, reply.Body) : Json(HttpStatusCode.OK, "{}");
            }

            lock (_routes)
            {
                return request.Method == HttpMethod.Get && _routes.TryGetValue(path, out var body)
                    ? Json(HttpStatusCode.OK, body)
                    : Json(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}""");
            }
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
