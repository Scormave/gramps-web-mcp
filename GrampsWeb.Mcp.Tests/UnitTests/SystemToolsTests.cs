using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Formatters;
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
        Assert.Contains("Edit Person", result);
    }

    [Fact]
    public async Task GetRecentChanges_Shows_Time_User_And_Changed_Objects()
    {
        var changes = string.Join(",", Enumerable.Range(1, SystemFormatter.MaxChangesShownPerTransaction + 2)
            .Select(i => $$"""{"id":{{i}},"obj_class":"Event","trans_type":0,"obj_handle":"e{{i}}","timestamp":1759242125.0}"""));
        var history = $$"""
            [
              {
                "id": 42, "description": "Edit Person", "first": 7, "last": 8, "undo": false, "timestamp": 1759242125.5,
                "connection": { "id": 3, "timestamp": 1759242120.0, "user": { "name": "jdoe", "full_name": "Jane Doe" } },
                "changes": [
                  { "id": 7, "obj_class": "Person", "trans_type": 1, "obj_handle": "p1", "ref_handle": null, "timestamp": 1759242125.5 },
                  { "id": 8, "obj_class": "Note", "trans_type": 2, "obj_handle": "n1", "ref_handle": null, "timestamp": 1759242125.5 }
                ]
              },
              {
                "id": 41, "description": "Import", "first": null, "last": null, "undo": true, "timestamp": 1759242000,
                "connection": { "id": 2, "timestamp": 1759242000, "user": null },
                "changes": [{{changes}}]
              }
            ]
            """;
        using var handler = new HistoryHandler(history, total: 345);
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = (await SystemTools.GetRecentChanges(limit: 2, client)).Replace("\r\n", "\n");

        Assert.StartsWith("RECENT CHANGES (2 of 345, newest first)\n", result);
        Assert.Contains("1. 2025-09-30 14:22:05 UTC — Edit Person — by Jane Doe [transaction: 42]\n" +
                        "   Updated Person [handle: p1]\n" +
                        "   Deleted Note [handle: n1]\n", result);
        Assert.Contains("2. 2025-09-30 14:20:00 UTC — Import (undo) [transaction: 41]\n   Added Event [handle: e1]\n", result);
        Assert.Contains("   … (+2 more changes)", result);
        Assert.DoesNotContain("[handle: e11]", result);
    }

    private sealed class HistoryHandler(string body = """[{"id":1,"description":"Edit Person"}]""", int? total = null) : HttpMessageHandler
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
                var response = JsonResponse(body);
                if (total.HasValue)
                    response.Headers.Add("X-Total-Count", total.Value.ToString());
                return Task.FromResult(response);
            }

            throw new Xunit.Sdk.XunitException($"Unexpected request: {path}");
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
