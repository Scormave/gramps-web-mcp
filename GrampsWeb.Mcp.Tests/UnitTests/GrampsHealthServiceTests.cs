using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Health;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsHealthServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsHealthy_WhenMetadataIsReachable()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler);

        var status = await service.CheckAsync();

        Assert.True(status.IsHealthy);
        Assert.Equal("https://gramps.example", status.ApiUrl);
        Assert.Equal("configured-tree", status.ConfiguredTreeId);
        Assert.Equal("Example Tree", status.TreeName);
        Assert.Equal("5f850009", status.TreeDatabaseId);
        Assert.Equal("6.0.0", status.GrampsVersion);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUnhealthy_WhenTokenRequestFails()
    {
        var handler = new RecordingHandler(failToken: true);
        var service = CreateService(handler);

        var status = await service.CheckAsync();

        Assert.False(status.IsHealthy);
        Assert.Equal("https://gramps.example", status.ApiUrl);
        Assert.NotNull(status.Error);
        Assert.Contains("Failed to obtain token", status.Error);
    }

    private static GrampsHealthService CreateService(RecordingHandler handler)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://gramps.example",
            Username: "owner",
            Password: "secret",
            TreeId: "configured-tree");

        return new GrampsHealthService(
            new HttpClient(handler) { BaseAddress = new Uri(config.ApiUrl) },
            config,
            NullLogger<GrampsHealthService>.Instance);
    }

    private sealed class RecordingHandler(bool failToken = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/token/")
            {
                if (failToken)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("invalid credentials", Encoding.UTF8, "text/plain")
                    });
                }

                return Task.FromResult(JsonResponse("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/metadata/")
            {
                return Task.FromResult(JsonResponse("""
                    {
                      "database": {
                        "id": "5f850009",
                        "name": "Example Tree"
                      },
                      "gramps": {
                        "version": "6.0.0"
                      }
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
