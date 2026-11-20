using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class ListRecordsSourceFilterTests
{
    public ListRecordsSourceFilterTests()
    {
        HandleCache.Invalidate();
    }

    [Fact]
    public async Task ListRecords_Citations_FiltersBySourceHandleThroughMatchesQuery()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListRecords("citations", sourceHandle: "102a49c11b31375183b9ad45a991", client: client);

        var query = Query(handler);
        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["source_handle == '102a49c11b31375183b9ad45a991'"]}]}""",
            query["rules"]);
        Assert.Null(query["source_handle"]);
        Assert.Null(query["extend"]);
        Assert.Equal("self", query["profile"]);
    }

    [Fact]
    public async Task ListRecords_Citations_SendsTheQueryBesideTheSourceRule()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListRecords(
            "citations",
            sourceHandle: "9d11cde4-53e4-4248-a627-ba38e55f051f",
            query: "confidence >= 3",
            client: client);

        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["confidence >= 3"]},{"name":"MatchesQuery","values":["source_handle == '9d11cde4-53e4-4248-a627-ba38e55f051f'"]}]}""",
            Query(handler)["rules"]);
    }

    [Fact]
    public async Task ListRecords_Citations_AddsTheSourceRuleToPlainAndRules()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListRecords(
            "citations",
            sourceHandle: "source-handle-1",
            rules: Rules("""[{"name": "HasTag", "values": ["ToDo"]}]"""),
            client: client);

        Assert.Equal(
            """{"rules":[{"name":"HasTag","values":["ToDo"]},{"name":"MatchesQuery","values":["source_handle == 'source-handle-1'"]}]}""",
            Query(handler)["rules"]);
    }

    [Theory]
    [InlineData("""{"function": "or", "rules": [{"name": "HasTag", "values": ["ToDo"]}, {"name": "HasNote"}]}""",
        """{"rules":[{"function":"or","rules":[{"name":"HasTag","values":["ToDo"]},{"name":"HasNote"}]},{"name":"MatchesQuery","values":["source_handle == 'source-handle-1'"]}]}""")]
    [InlineData("""{"invert": true, "rules": [{"name": "HasTag", "values": ["ToDo"]}]}""",
        """{"rules":[{"invert":true,"rules":[{"name":"HasTag","values":["ToDo"]}]},{"name":"MatchesQuery","values":["source_handle == 'source-handle-1'"]}]}""")]
    public async Task ListRecords_Citations_NestsOtherRulesBesideTheSourceRule(string rules, string expected)
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListRecords("citations", sourceHandle: "source-handle-1", rules: Rules(rules), client: client);

        Assert.Equal(expected, Query(handler)["rules"]);
    }

    [Fact]
    public async Task ListRecords_Citations_ResolvesSourceGrampsId()
    {
        using var handler = new CitationListHandler
        {
            SourceLookupResponse = """[{"handle": "source-handle-1", "gramps_id": "S0001"}]"""
        };
        var client = CreateClient(handler);

        await SearchTools.ListRecords("citations", sourceHandle: "S0001", client: client);

        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["source_handle == 'source-handle-1'"]}]}""",
            Query(handler)["rules"]);
    }

    [Fact]
    public async Task ListRecords_Citations_ResolvesSourceGrampsIdInACustomFormat()
    {
        using var handler = new CitationListHandler
        {
            SourceLookupResponse = """[{"handle": "source-handle-1", "gramps_id": "S_ABC"}]"""
        };
        var client = CreateClient(handler);

        var result = await SearchTools.ListRecords("citations", sourceHandle: "S_ABC", client: client);

        Assert.Equal(
            """{"rules":[{"name":"MatchesQuery","values":["source_handle == 'source-handle-1'"]}]}""",
            Query(handler)["rules"]);
        Assert.Equal("No citations found.", result);
    }

    [Theory]
    [InlineData("S_ABC")]
    [InlineData("S0099")]
    [InlineData("102a49c11b31375183b9ad45a991")]
    public async Task ListRecords_Citations_SaysWhenTheSourceDoesNotExist(string sourceHandle)
    {
        using var handler = new CitationListHandler { SourceExists = false };
        var client = CreateClient(handler);

        var result = await SearchTools.ListRecords("citations", sourceHandle: sourceHandle, client: client);

        Assert.StartsWith($"Source not found: {sourceHandle}", result);
    }

    [Fact]
    public async Task ListRecords_Citations_RejectsSourceHandleThatWouldBreakTheQuery()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<McpException>(() =>
            SearchTools.ListRecords("citations", sourceHandle: "x' or gramps_id != 'C", client: client));

        Assert.Contains("sourceHandle", error.Message);
        Assert.Null(handler.CitationPath);
    }

    private static FlexibleFilterRules Rules(string json) =>
        JsonSerializer.Deserialize<FlexibleFilterRules>(json)!;

    private static NameValueCollection Query(CitationListHandler handler) =>
        HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.CitationPath).Query);

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") };
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var provider = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, provider);
    }

    private sealed class CitationListHandler : HttpMessageHandler
    {
        public string SourceLookupResponse { get; init; } = "[]";

        /// <summary>Whether the source a filter names exists, read when its page of citations is empty.</summary>
        public bool SourceExists { get; init; } = true;

        public string? CitationPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            if (path.StartsWith("/api/sources/?gramps_id=", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(SourceLookupResponse));

            if (path.StartsWith("/api/sources/", StringComparison.Ordinal) && path.EndsWith("?keys=handle", StringComparison.Ordinal))
            {
                return Task.FromResult(SourceExists
                    ? JsonResponse("""{"handle": "found"}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            Assert.StartsWith("/api/citations/?", path);
            CitationPath = path;
            return Task.FromResult(JsonResponse("[]"));
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
