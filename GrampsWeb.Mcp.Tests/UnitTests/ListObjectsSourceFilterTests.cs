using System.Net;
using System.Text;
using System.Web;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class ListObjectsSourceFilterTests
{
    public ListObjectsSourceFilterTests()
    {
        HandleCache.Invalidate();
    }

    [Fact]
    public async Task ListObjects_Citations_FiltersBySourceHandleThroughGql()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListObjects("citations", sourceHandle: "102a49c11b31375183b9ad45a991", client: client);

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.CitationPath).Query);
        Assert.Equal("source_handle = \"102a49c11b31375183b9ad45a991\"", query["gql"]);
        Assert.Null(query["source_handle"]);
        Assert.Null(query["extend"]);
        Assert.Equal("self", query["profile"]);
    }

    [Fact]
    public async Task ListObjects_Citations_CombinesSourceFilterWithUserGql()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        await SearchTools.ListObjects(
            "citations",
            sourceHandle: "9d11cde4-53e4-4248-a627-ba38e55f051f",
            gql: "confidence >= 3 or page ~ 12",
            client: client);

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.CitationPath).Query);
        Assert.Equal(
            "source_handle = \"9d11cde4-53e4-4248-a627-ba38e55f051f\" and (confidence >= 3 or page ~ 12)",
            query["gql"]);
    }

    [Fact]
    public async Task ListObjects_Citations_ResolvesSourceGrampsId()
    {
        using var handler = new CitationListHandler
        {
            SourceLookupResponse = """[{"handle": "source-handle-1", "gramps_id": "S0001"}]"""
        };
        var client = CreateClient(handler);

        await SearchTools.ListObjects("citations", sourceHandle: "S0001", client: client);

        var query = HttpUtility.ParseQueryString(new Uri("https://gramps-web.test" + handler.CitationPath).Query);
        Assert.Equal("source_handle = \"source-handle-1\"", query["gql"]);
    }

    [Fact]
    public async Task ListObjects_Citations_RejectsSourceHandleThatWouldBreakGql()
    {
        using var handler = new CitationListHandler();
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<McpException>(() =>
            SearchTools.ListObjects("citations", sourceHandle: "x\" or gramps_id ~ C", client: client));

        Assert.Contains("sourceHandle", error.Message);
        Assert.Null(handler.CitationPath);
    }

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

        public string? CitationPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            if (path.StartsWith("/api/sources/?gramps_id=", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(SourceLookupResponse));

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
