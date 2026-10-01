using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class SystemToolsTests
{
    [Fact]
    public async Task GetRecentChanges_RequestsFirstPage_SoPageSizeIsApplied()
    {
        using var handler = new HistoryHandler();
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = await SystemTools.GetRecentChanges(limit: 5, client);

        Assert.Equal("/api/transactions/history/?page=1&pagesize=5&sort=-id", handler.HistoryPath);
        Assert.Contains("description: Edit Person", result);
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public string? HistoryPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path == "/api/token/")
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));

            if (request.RequestUri.AbsolutePath == "/api/transactions/history/")
            {
                HistoryPath = path;
                return Task.FromResult(JsonResponse("""[{"id":1,"description":"Edit Person"}]"""));
            }

            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
