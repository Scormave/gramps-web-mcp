using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class PersonToolsHandleResolutionTests
{
    public PersonToolsHandleResolutionTests()
    {
        HandleCache.Invalidate();
    }

    [Fact]
    public async Task GetAncestors_ResolvesGrampsIdBeforeTraversal()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var result = await PersonTools.GetAncestors("I0002", generations: 10, client: client);

        Assert.Contains("No ancestors found for I0002", result);
        Assert.Equal(1, handler.RequestCount("/api/people/?gramps_id=I0002&pagesize=1"));
        Assert.Equal(1, handler.RequestCount("/api/people/person-handle-2"));
        Assert.Equal(0, handler.RequestCount("/api/people/I0002"));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree");

        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);

        return new GrampsApiClient(
            new HttpClient(handler),
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<string> _paths = [];

        public int RequestCount(string path)
        {
            lock (_gate)
            {
                return _paths.Count(p => p == path);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            lock (_gate)
            {
                _paths.Add(path);
            }

            if (request.Method == HttpMethod.Post && path.StartsWith("/api/token/", StringComparison.Ordinal))
            {
                return Task.FromResult(JsonResponse("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """));
            }

            if (request.Method == HttpMethod.Get && path == "/api/people/?gramps_id=I0002&pagesize=1")
            {
                return Task.FromResult(JsonResponse("""
                    [{"handle": "person-handle-2", "gramps_id": "I0002"}]
                    """));
            }

            if (request.Method == HttpMethod.Get && path == "/api/people/person-handle-2")
            {
                return Task.FromResult(JsonResponse("""
                    {
                      "handle": "person-handle-2",
                      "gramps_id": "I0002",
                      "gender": 2,
                      "parent_family_list": []
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            });
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
