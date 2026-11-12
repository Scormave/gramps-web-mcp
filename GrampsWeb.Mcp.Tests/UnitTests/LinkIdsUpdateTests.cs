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

/// <summary>
/// The update tools look up the Gramps IDs in their link fields among the records each field links, before the
/// update starts, and change nothing when one of them is not in the tree.
/// </summary>
public class LinkIdsUpdateTests
{
    private const string StoredFamily = """
        {
          "_class": "Family", "handle": "h-family", "gramps_id": "F0300", "father_handle": "h-old-father", "type": "Married",
          "child_ref_list": [{ "_class": "ChildRef", "ref": "h-child-1", "frel": "Birth", "mrel": "Birth" },
                             { "_class": "ChildRef", "ref": "h-child-2", "frel": "Birth", "mrel": "Birth" }],
          "note_list": ["h-note-1"]
        }
        """;

    [Fact]
    public async Task UpdateFamily_Adds_Links_Given_By_Gramps_Id()
    {
        var handler = new TreeHandler(StoredFamily, new Dictionary<string, string>
        {
            ["/api/people/?gramps_id=I0301&pagesize=1"] = """[{ "handle": "h-father" }]""",
            ["/api/notes/?gramps_id=N0301&pagesize=1"] = """[{ "handle": "h-note-2" }]"""
        });

        await FamilyTools.UpdateFamily("h-family", fatherHandle: "I0301",
            noteHandles: new FlexibleHandleList { Handles = ["N0301"] }, client: CreateClient(handler), linkMode: "add");

        Assert.Equal("h-father", handler.Body!["father_handle"]!.GetValue<string>());
        Assert.Equal(["h-note-1", "h-note-2"], handler.Body["note_list"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateFamily_Removes_A_Child_Given_By_Gramps_Id()
    {
        var handler = new TreeHandler(StoredFamily, new Dictionary<string, string>
        {
            ["/api/people/?gramps_id=I0302&pagesize=1"] = """[{ "handle": "h-child-2" }]"""
        });

        await FamilyTools.UpdateFamily("h-family", childRefs: JsonSerializer.Deserialize<FlexibleChildRefList>("""["I0302"]"""),
            client: CreateClient(handler), linkMode: "remove");

        Assert.Equal(["h-child-1"],
            handler.Body!["child_ref_list"]!.AsArray().Select(c => c!["ref"]!.GetValue<string>()));
    }

    [Fact]
    public async Task UpdateEvent_Names_Each_Unknown_Id_And_Changes_Nothing()
    {
        var handler = new TreeHandler("""{ "_class": "Event", "handle": "h-event", "gramps_id": "E0300", "type": "Birth" }""");

        var error = await Assert.ThrowsAsync<McpException>(() => EventTools.UpdateEvent("h-event", placeHandle: "P0399",
            citationHandles: new FlexibleHandleList { Handles = ["S0399"] }, client: CreateClient(handler)));

        Assert.Equal(
            "Error: placeHandle: there is no place P0399. citationHandles: there is no citation S0399. S is the prefix " +
            "of sources, and citationHandles links citations. Nothing was changed.",
            error.Message);
        Assert.Null(handler.Body);
        Assert.DoesNotContain(handler.Requests, r => r.StartsWith("/api/events/h-event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateCitation_Moves_The_Citation_To_A_Source_Given_By_Gramps_Id()
    {
        var handler = new TreeHandler(
            """{ "_class": "Citation", "handle": "h-citation", "gramps_id": "C0300", "source_handle": "h-old-source" }""",
            new Dictionary<string, string> { ["/api/sources/?gramps_id=S0301&pagesize=1"] = """[{ "handle": "h-source" }]""" });

        await CitationTools.UpdateCitation("h-citation", sourceHandle: "S0301", client: CreateClient(handler));

        Assert.Equal("h-source", handler.Body!["source_handle"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateCitation_Refuses_A_Source_Id_The_Tree_Lacks()
    {
        var handler = new TreeHandler("""{ "_class": "Citation", "handle": "h-citation", "gramps_id": "C0300" }""");

        var error = await Assert.ThrowsAsync<McpException>(() =>
            CitationTools.UpdateCitation("h-citation", sourceHandle: "S0398", client: CreateClient(handler)));

        Assert.Equal("Error: sourceHandle: there is no source S0398. Nothing was changed.", error.Message);
        Assert.Null(handler.Body);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://link-ids-update.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers Gramps ID lookups from <paramref name="lookups"/> (404 otherwise, as Gramps Web does), every other GET
    /// with the stored record, and records the PUT body.
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

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
