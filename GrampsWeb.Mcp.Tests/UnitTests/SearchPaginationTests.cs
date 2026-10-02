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

public class SearchPaginationTests
{
    [Fact]
    public async Task Search_ServerError_ProvidesRecoveryWithoutHtml()
    {
        using var handler = new SearchErrorHandler();
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var provider = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, provider);

        var error = await Assert.ThrowsAsync<McpException>(() =>
            SearchTools.Search("Р-6143 Оп. 2 Д. 839", 1, 30, client));

        Assert.Contains("HTTP 500", error.Message);
        Assert.Contains("shorter term", error.Message);
        Assert.DoesNotContain("<html>", error.Message);
        Assert.Equal(1, handler.SearchRequests);
    }

    [Theory]
    [InlineData(false, true, 3, 20, 5, 45, 3, 41)]
    [InlineData(true, true, 3, 20, 5, 45, 3, 41)]
    [InlineData(true, false, 3, 20, 5, 45, 0, 41)]
    [InlineData(false, true, 2, 20, 20, 40, 2, 21)]
    [InlineData(false, true, 1, 20, 5, 5, 1, 1)]
    [InlineData(false, true, 3, 7, 2, 16, 3, 15)]
    public async Task ListObjects_UsesRequestedPageSizeForPageCountAndRowNumbers(
        bool arrayResponse, bool includeTotal, int page, int pageSize,
        int count, int total, int expectedPages, int firstRow)
    {
        var objects = Enumerable.Range(1, count)
            .Select(i => new { handle = $"tag-{i}", name = $"Tag {i}" }).ToArray();
        var body = arrayResponse
            ? JsonSerializer.Serialize(objects)
            : JsonSerializer.Serialize(new { objects, page, total });
        using var handler = new ListHandler(body, arrayResponse && includeTotal ? total : null);
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var provider = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, provider);

        var result = await SearchTools.ListObjects("tags", page, pageSize, client: client);

        Assert.Equal($"/api/tags/?page={page}&pagesize={pageSize}", handler.ListPath);
        Assert.Contains(expectedPages > 0
            ? $"Page {page} of {expectedPages}, Total: {total}"
            : $"Page {page}, Total: unknown", result);
        var rows = result.Split('\n').Where(line => line.Contains(". Tag: ")).ToArray();
        Assert.Equal(count, rows.Length);
        for (var i = 0; i < rows.Length; i++)
            Assert.StartsWith($"{firstRow + i}. Tag: ", rows[i]);
        if (expectedPages == 0)
            Assert.DoesNotContain($"Page {page} of", result);
    }

    [Theory]
    [InlineData(2, 2, 5, "Search Results (Page 2 of 3, Total: 5):")]
    [InlineData(1, 20, 2, "Search Results (Page 1 of 1, Total: 2):")]
    [InlineData(1, 20, null, "Search Results (2):")]
    public async Task Search_Header_Shows_Page_Count_And_Total(int page, int pageSize, int? total, string expectedHeader)
    {
        const string hits = """
            [
              { "handle": "t1", "object_type": "tag", "object": { "handle": "t1", "name": "Reviewed" } },
              { "handle": "t2", "object_type": "tag", "object": { "handle": "t2", "name": "Todo" } }
            ]
            """;
        using var handler = new SearchHandler(hits, total);

        var result = await SearchTools.Search("tag", page, pageSize, Client(handler));

        Assert.Equal($"/api/search/?query=tag&page={page}&pagesize={pageSize}&profile=self", handler.SearchPath);
        Assert.StartsWith(expectedHeader, result);
    }

    [Fact]
    public async Task Search_Page_Past_The_End_Reports_Total()
    {
        using var handler = new SearchHandler("[]", 5);

        var result = await SearchTools.Search("tag", 9, 2, Client(handler));

        Assert.Equal("No results on page 9 for 'tag' (Total: 5); try a lower page.", result);
    }

    private static GrampsApiClient Client(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var provider = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, provider);
    }

    private sealed class SearchHandler(string body, int? total) : HttpMessageHandler
    {
        public string? SearchPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            if (path.StartsWith("/api/search/", StringComparison.Ordinal))
            {
                SearchPath = path;
                var response = JsonResponse(body);
                if (total.HasValue)
                    response.Headers.Add("X-Total-Count", total.Value.ToString());
                return Task.FromResult(response);
            }

            // Type-label lookups for the formatter.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ListHandler(string body, int? total) : HttpMessageHandler
    {
        public string? ListPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            Assert.StartsWith("/api/tags/?", path);
            ListPath = path;
            var response = JsonResponse(body);
            if (total.HasValue)
                response.Headers.Add("X-Total-Count", total.Value.ToString());
            return Task.FromResult(response);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SearchErrorHandler : HttpMessageHandler
    {
        public int SearchRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""")
                });

            Assert.Equal("/api/search/", request.RequestUri.AbsolutePath);
            SearchRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("<html>Internal Server Error</html>")
            });
        }
    }
}
