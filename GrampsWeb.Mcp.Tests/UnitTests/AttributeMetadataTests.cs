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
/// Attributes of people, families, events and media (Gramps <c>Attribute</c>) carry citations, notes and privacy;
/// attributes of sources and citations (Gramps <c>SrcAttribute</c>) carry only type, value and privacy.
/// </summary>
public class AttributeMetadataTests
{
    [Fact]
    public async Task CreatePerson_Sends_Attribute_Citations_Notes_And_Privacy()
    {
        var handler = new RecordingHandler("Person");

        await PersonTools.CreatePerson(
            Deserialize<FlexibleGrampsName>("""{ "given": "Ivan", "surname": "Petrov" }"""),
            attributes: Deserialize<FlexibleAttributeList>("""
                [{ "type": "Occupation", "value": "Smith", "citation_list": ["c1"], "note_list": ["n1"], "private": true },
                 "Nickname: Vanya"]
                """),
            client: CreateClient(handler));

        AssertJsonEqual(JsonNode.Parse("""
            [{ "type": "Occupation", "value": "Smith", "citation_list": ["c1"], "note_list": ["n1"], "private": true },
             { "type": "Nickname", "value": "Vanya", "private": false }]
            """), handler.PostBody!["attribute_list"]);
    }

    [Fact]
    public async Task CreateSource_Sends_Attribute_Privacy_Without_Empty_Link_Lists()
    {
        var handler = new RecordingHandler("Source");

        await SourceTools.CreateSource("Parish register",
            attributes: Deserialize<FlexibleAttributeList>("""[{ "type": "Pages", "value": "120", "citation_list": [], "private": true }]"""),
            client: CreateClient(handler));

        AssertJsonEqual(JsonNode.Parse("""[{ "type": "Pages", "value": "120", "private": true }]"""),
            handler.PostBody!["attribute_list"]);
    }

    [Theory]
    [InlineData("create_source")]
    [InlineData("update_source")]
    [InlineData("create_citation")]
    [InlineData("update_citation")]
    public async Task Source_And_Citation_Attributes_With_Citations_Or_Notes_Are_Rejected_Before_Anything_Is_Sent(string tool)
    {
        var handler = new RecordingHandler(tool.EndsWith("source") ? "Source" : "Citation");
        var client = CreateClient(handler);
        var attributes = Deserialize<FlexibleAttributeList>("""[{ "type": "Pages", "value": "120", "note_list": ["n1"] }]""");

        var error = await Assert.ThrowsAsync<McpException>(() => tool switch
        {
            "create_source" => SourceTools.CreateSource("Parish register", attributes: attributes, client: client),
            "update_source" => SourceTools.UpdateSource("h-source", attributes: attributes, client: client),
            "create_citation" => CitationTools.CreateCitation("h-source", attributes: attributes, client: client),
            _ => CitationTools.UpdateCitation("h-citation", attributes: attributes, client: client)
        });

        Assert.Contains("'Pages: 120'", error.Message);
        Assert.Contains("only a type, a value and the private flag", error.Message);
        Assert.Empty(handler.Requests);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    private static void AssertJsonEqual(JsonNode? expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected:\n{expected}\nActual:\n{actual}");

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://attribute-metadata.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Answers the token route and the type vocabularies (empty, so attribute types go unchecked), records every other
    /// request and answers a create with a new object of <paramref name="grampsClass"/>.
    /// </summary>
    private sealed class RecordingHandler(string grampsClass) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        public JsonNode? PostBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/"))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");
            if (request.RequestUri.AbsolutePath.StartsWith("/api/types/"))
                return Json("{}");

            Requests.Add($"{request.Method} {request.RequestUri.AbsolutePath}");
            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            PostBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Json($$"""[{ "_class": "{{grampsClass}}", "type": "add", "old": null, "new": { "_class": "{{grampsClass}}", "handle": "h-new", "gramps_id": "X0001" } }]""");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
