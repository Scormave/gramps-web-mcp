using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsAuthTokenProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_FallsBackToNewToken_WhenRefreshReturns401()
    {
        var handler = new AuthHandler
        {
            RefreshStatusCode = HttpStatusCode.Unauthorized,
            InitialExpiresIn = 0
        };
        var provider = CreateProvider(handler);

        await provider.GetTokenAsync();
        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("new-access", token);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/api/token/", request.Path),
            request => Assert.Equal("/api/token/refresh/", request.Path),
            request => Assert.Equal("/api/token/", request.Path));
    }

    [Fact]
    public async Task RefreshTokenAsync_FallsBackToNewToken_WhenRefreshReturns401()
    {
        var handler = new AuthHandler
        {
            RefreshStatusCode = HttpStatusCode.Unauthorized,
            InitialExpiresIn = 0
        };
        var provider = CreateProvider(handler);

        await provider.GetTokenAsync();
        var token = await provider.RefreshTokenAsync();

        Assert.Equal("new-access", token);
        Assert.Collection(
            handler.Requests,
            request => Assert.Equal("/api/token/", request.Path),
            request => Assert.Equal("/api/token/refresh/", request.Path),
            request => Assert.Equal("/api/token/", request.Path));
    }

    [Fact]
    public async Task GetAccessTokenAsync_Throws_WhenRefreshFailsWithNonUnauthorizedStatus()
    {
        var handler = new AuthHandler
        {
            RefreshStatusCode = HttpStatusCode.InternalServerError,
            InitialExpiresIn = 0
        };
        var provider = CreateProvider(handler);

        await provider.GetTokenAsync();

        var ex = await Assert.ThrowsAsync<GrampsApiException>(() => provider.GetAccessTokenAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Single(handler.Requests, r => r.Path == "/api/token/refresh/");
        Assert.Single(handler.Requests, r => r.Path == "/api/token/");
    }

    private static GrampsAuthTokenProvider CreateProvider(AuthHandler handler)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree");

        return new GrampsAuthTokenProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") },
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
    }

    private sealed class AuthHandler : HttpMessageHandler
    {
        private int _tokenRequestCount;

        public HttpStatusCode RefreshStatusCode { get; init; } = HttpStatusCode.OK;

        public int InitialExpiresIn { get; init; } = 900;

        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Requests.Add(new RecordedRequest(path));

            if (request.Method == HttpMethod.Post && path == "/api/token/")
            {
                _tokenRequestCount++;
                var accessToken = _tokenRequestCount == 1 ? "old-access" : "new-access";
                var refreshToken = _tokenRequestCount == 1 ? "stale-refresh" : "new-refresh";
                var expiresIn = _tokenRequestCount == 1 ? InitialExpiresIn : 900;

                return Task.FromResult(JsonResponse($$"""
                    {
                      "access_token": "{{accessToken}}",
                      "refresh_token": "{{refreshToken}}",
                      "expires_in": {{expiresIn}}
                    }
                    """));
            }

            if (request.Method == HttpMethod.Post && path == "/api/token/refresh/")
            {
                if (RefreshStatusCode == HttpStatusCode.OK)
                {
                    return Task.FromResult(JsonResponse("""
                        {
                          "access_token": "refreshed-access",
                          "expires_in": 900
                        }
                        """));
                }

                return Task.FromResult(new HttpResponseMessage(RefreshStatusCode)
                {
                    Content = new StringContent("Unauthorized", Encoding.UTF8, "text/plain")
                });
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

    private sealed record RecordedRequest(string Path);
}
