using System.Collections.Concurrent;
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

/// <summary>The person and family tools check the roles of the event links they send.</summary>
public class EventRoleTests
{
    [Fact]
    public async Task CreateRecords_Rejects_A_Misspelt_Role_Before_Saving()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => CreatePerson("""["e-birth", "e-baptism::Witnes"]""", handler));

        Assert.StartsWith("Error: people[0]: eventRefs: Invalid event role types 'Witnes'. Did you mean: Witness?", error.Message);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task CreateRecords_Stores_Roles_In_Their_Known_Spelling()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await CreatePerson("""["e-baptism:: witness", { "ref": "e-baptism-2", "role": "восприемник" }]""", handler);

        Assert.Equal(
            ["Witness", "Восприемник"],
            CreateRecordsCall.Saved(handler.Body, "Person")["event_ref_list"]!.AsArray().Select(r => r!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task CreateRecords_Gives_A_Persons_Bare_Event_Handle_The_Primary_Role()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await CreatePerson("""["e-birth", "e-baptism::Witness"]""", handler);

        Assert.Equal(
            ["Primary", "Witness"],
            CreateRecordsCall.Saved(handler.Body, "Person")["event_ref_list"]!.AsArray().Select(r => r!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task CreateRecords_Gives_Family_Event_Links_Without_A_Role_The_Family_Role()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        await CreateRecordsCall.RunAsync(
            """{ "families": [{ "eventRefs": ["e-marriage", { "ref": "e-banns" }, "e-engagement::primary"] }] }""",
            CreateClient(handler));

        Assert.Equal(
            ["Family", "Family", "Primary"],
            CreateRecordsCall.Saved(handler.Body, "Family")["event_ref_list"]!.AsArray().Select(r => r!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateFamily_Replace_Gives_Bare_Handles_The_Family_Role_And_Keeps_Stored_Roles_Of_Link_Objects()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                {
                  "handle": "h-family", "gramps_id": "F0100",
                  "event_ref_list": [{ "ref": "e-marriage", "role": "Primary" }, { "ref": "e-banns", "role": "Восприемник" }]
                }
                """
        });

        await FamilyTools.UpdateFamily(
            "h-family", eventRefs: Refs("""["e-marriage", { "ref": "e-banns" }, { "ref": "e-divorce" }]"""),
            client: CreateClient(handler));

        Assert.Equal(
            ["Family", "Восприемник", "Family"],
            handler.Body!["event_ref_list"]!.AsArray().Select(r => r!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateFamily_Add_Gives_New_Links_The_Family_Role_And_Leaves_Stored_Ones()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/families/h-family"] = """
                {
                  "handle": "h-family", "gramps_id": "F0100",
                  "event_ref_list": [{ "ref": "e-marriage", "role": "Primary" }]
                }
                """
        });

        await FamilyTools.UpdateFamily(
            "h-family", eventRefs: Refs("""["e-marriage", { "ref": "e-divorce" }]"""), client: CreateClient(handler),
            linkMode: "add");

        Assert.Equal(
            ["Primary", "Family"],
            handler.Body!["event_ref_list"]!.AsArray().Select(r => r!["role"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateFamily_Rejects_An_Unknown_Role_Without_Reading_Or_Saving_The_Family()
    {
        var handler = new TreeHandler(new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<McpException>(() => FamilyTools.UpdateFamily(
            "h-family", eventRefs: Refs("""["e-marriage::Brides father"]"""), client: CreateClient(handler), linkMode: "add"));

        Assert.Contains("Invalid event role types 'Brides father'", error.Message);
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/families/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdatePerson_Remove_Does_Not_Check_Roles()
    {
        var handler = new TreeHandler(new Dictionary<string, string>
        {
            ["/api/people/h-person"] = """
                {
                  "handle": "h-person", "gramps_id": "I0100",
                  "event_ref_list": [{ "ref": "e-birth", "role": "Primary" }, { "ref": "e-old", "role": "Witness" }]
                }
                """
        });

        await PersonTools.UpdatePerson(
            "h-person", eventRefs: Refs("""["e-old::Anything"]"""), client: CreateClient(handler), linkMode: "remove");

        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/types/", StringComparison.Ordinal));
        Assert.Equal(["e-birth"], handler.Body!["event_ref_list"]!.AsArray().Select(r => r!["ref"]!.GetValue<string>()));
    }

    /// <summary>Creates Ivan Petrov with <paramref name="eventRefs"/> through create_records.</summary>
    private static Task<string> CreatePerson(string eventRefs, TreeHandler handler) => CreateRecordsCall.RunAsync(
        $$"""{ "people": [{ "primaryName": { "given": "Ivan", "surname": "Petrov" }, "eventRefs": {{eventRefs}} }] }""",
        CreateClient(handler));

    private static FlexibleEventRefList Refs(string json) => JsonSerializer.Deserialize<FlexibleEventRefList>(json)!;

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://event-roles.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves the role vocabularies (one custom role), no people for the duplicate check and bodies by path and query
    /// (404 otherwise), and records the POST or PUT body.
    /// </summary>
    private sealed class TreeHandler(IReadOnlyDictionary<string, string> bodies) : HttpMessageHandler
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
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                return Json(request.Method == HttpMethod.Put ? "{}" : CreateRecordsCall.Changes(Body!).ToJsonString());
            }

            return path switch
            {
                "/api/types/default/" => Json("""{ "event_role_types": ["Unknown", "Primary", "Witness", "Godparent", "Father", "Mother", "Family"] }"""),
                "/api/types/custom/" => Json("""{ "event_role_types": ["Восприемник"] }"""),
                _ when path.StartsWith("/api/people/?rules=", StringComparison.Ordinal) => Json("[]"),
                _ => bodies.TryGetValue(path, out var body) ? Json(body) : new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
