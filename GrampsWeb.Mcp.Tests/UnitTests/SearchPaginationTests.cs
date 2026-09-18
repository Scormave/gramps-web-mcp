using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class SearchPaginationTests
{
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
}
