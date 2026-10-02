using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
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

    [Fact]
    public async Task CheckAsync_UsesRefreshToken_WhenConfigured()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler, refreshToken: "configured-refresh");

        var status = await service.CheckAsync();

        Assert.True(status.IsHealthy);
        Assert.Equal("Bearer configured-refresh", handler.RefreshAuthorization);
        Assert.Equal(0, handler.CredentialPosts);
    }

    [Fact]
    public async Task CheckAsync_Reuses_The_Cached_Token()
    {
        var handler = new RecordingHandler();
        var service = CreateService(handler);

        Assert.True((await service.CheckAsync()).IsHealthy);
        Assert.True((await service.CheckAsync()).IsHealthy);

        Assert.Equal(1, handler.CredentialPosts);
        Assert.Equal(2, handler.MetadataReads);
    }

    [Fact]
    public async Task CheckAsync_Shares_Its_Token_With_Tool_Requests()
    {
        var handler = new RecordingHandler();
        var tokenProvider = CreateTokenProvider(handler, CreateConfig());
        var service = CreateService(handler, tokenProvider);
        var client = new GrampsApiClient(
            new HttpClient(handler), CreateConfig(), NullLogger<GrampsApiClient>.Instance, tokenProvider);

        Assert.True((await service.CheckAsync()).IsHealthy);
        await client.GetAsync<JsonElement>("/api/metadata/");

        Assert.Equal(1, handler.CredentialPosts);
    }

    [Fact]
    public async Task CheckAsync_Logs_In_Again_When_The_Cached_Token_Is_Rejected()
    {
        var handler = new RecordingHandler(rejectedTokens: ["token-1"]);
        var service = CreateService(handler);

        var status = await service.CheckAsync();

        Assert.True(status.IsHealthy);
        Assert.Equal(2, handler.CredentialPosts);
        Assert.Equal(2, handler.MetadataReads);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUnhealthy_When_A_Fresh_Token_Is_Rejected_Too()
    {
        var handler = new RecordingHandler(rejectedTokens: ["token-1", "token-2"]);
        var service = CreateService(handler);

        var status = await service.CheckAsync();

        Assert.False(status.IsHealthy);
        Assert.Contains("Failed to read metadata: Unauthorized", status.Error);
        Assert.Equal(2, handler.CredentialPosts);
    }

    private static GrampsConfig CreateConfig(string? refreshToken = null) => new(
        ApiUrl: "https://gramps.example",
        Username: "owner",
        Password: "secret",
        TreeId: "configured-tree",
        RefreshToken: refreshToken);

    private static GrampsAuthTokenProvider CreateTokenProvider(RecordingHandler handler, GrampsConfig config) =>
        new(new HttpClient(handler), config, NullLogger<GrampsAuthTokenProvider>.Instance);

    private static GrampsHealthService CreateService(RecordingHandler handler, string? refreshToken = null)
    {
        var config = CreateConfig(refreshToken);
        return CreateService(handler, CreateTokenProvider(handler, config), config);
    }

    private static GrampsHealthService CreateService(
        RecordingHandler handler,
        GrampsAuthTokenProvider tokenProvider,
        GrampsConfig? config = null)
    {
        config ??= CreateConfig();
        return new GrampsHealthService(
            new HttpClient(handler) { BaseAddress = new Uri(config.ApiUrl) },
            config,
            tokenProvider,
            NullLogger<GrampsHealthService>.Instance);
    }

    /// <summary>Issues "token-1", "token-2", … per login; metadata answers 401 to <paramref name="rejectedTokens"/>.</summary>
    private sealed class RecordingHandler(bool failToken = false, string[]? rejectedTokens = null) : HttpMessageHandler
    {
        public int CredentialPosts { get; private set; }

        public int MetadataReads { get; private set; }

        public string? RefreshAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/token/refresh/")
            {
                RefreshAuthorization = request.Headers.Authorization?.ToString();
                return Task.FromResult(JsonResponse("""
                    {
                      "access_token": "token",
                      "expires_in": 900
                    }
                    """));
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/token/")
            {
                CredentialPosts++;
                if (failToken)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("invalid credentials", Encoding.UTF8, "text/plain")
                    });
                }

                return Task.FromResult(JsonResponse($$"""
                    {
                      "access_token": "token-{{CredentialPosts}}",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/metadata/")
            {
                MetadataReads++;
                if (rejectedTokens?.Contains(request.Headers.Authorization?.Parameter) == true)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

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
