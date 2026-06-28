using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class HandleResolverTests
{
    public HandleResolverTests()
    {
        HandleCache.Invalidate();
    }

    [Theory]
    [InlineData("I0001", true)]
    [InlineData("F23", true)]
    [InlineData("P0005", true)]
    [InlineData("E1", true)]
    [InlineData("S999", true)]
    [InlineData("C0001", true)]
    [InlineData("R0001", true)]
    [InlineData("N0001", true)]
    [InlineData("M0001", true)]
    [InlineData("T0001", true)]
    [InlineData("abc123def456789012345678", false)]
    [InlineData("", false)]
    [InlineData("0001", false)]
    [InlineData("II001", false)]
    [InlineData("i0001", false)]
    [InlineData("A", false)]
    [InlineData("I", false)]
    [InlineData("I000000001", false)] // 10 chars, exceeds typical Gramps ID length
    public void LooksLikeGrampsId_DetectsCorrectly(string value, bool expected)
    {
        Assert.Equal(expected, HandleResolver.LooksLikeGrampsId(value));
    }

    [Theory]
    [InlineData('I', "people")]
    [InlineData('F', "families")]
    [InlineData('E', "events")]
    [InlineData('P', "places")]
    [InlineData('S', "sources")]
    [InlineData('C', "citations")]
    [InlineData('R', "repositories")]
    [InlineData('N', "notes")]
    [InlineData('M', "media")]
    [InlineData('T', "tags")]
    [InlineData('Z', null)]
    [InlineData('X', null)]
    [InlineData('i', null)]
    public void PrefixToObjectType_MapsCorrectly(char prefix, string? expected)
    {
        Assert.Equal(expected, HandleResolver.PrefixToObjectType(prefix));
    }

    [Fact]
    public async Task ResolveToHandleAsync_ResolvesFromArrayResponse_AndCachesSecondCall()
    {
        var handler = new ResolverRecordingHandler
        {
            ListResponses =
            {
                ["/api/people/"] = """
                    [{"handle": "person-handle-abc", "gramps_id": "I0001"}]
                    """
            }
        };
        var client = CreateClient(handler);

        var first = await HandleResolver.ResolveToHandleAsync("I0001", client);
        var second = await HandleResolver.ResolveToHandleAsync("I0001", client);

        Assert.Equal("person-handle-abc", first);
        Assert.Equal("person-handle-abc", second);
        Assert.Equal(1, handler.ListRequestCount("/api/people/"));
    }

    [Fact]
    public async Task ResolveToHandleAsync_ResolvesFromObjectsWrapperResponse()
    {
        var handler = new ResolverRecordingHandler
        {
            ListResponses =
            {
                ["/api/families/"] = """
                    {"objects": [{"handle": "family-handle-xyz", "gramps_id": "F0023"}]}
                    """
            }
        };
        var client = CreateClient(handler);

        var handle = await HandleResolver.ResolveToHandleAsync("F0023", client);

        Assert.Equal("family-handle-xyz", handle);
        Assert.Equal(1, handler.ListRequestCount("/api/families/"));
    }

    [Fact]
    public async Task ResolveToHandleAsync_PassesThroughOpaqueHandle_WithoutHttp()
    {
        var handler = new ResolverRecordingHandler();
        var client = CreateClient(handler);
        var opaqueHandle = "abc123def456789012345678";

        var result = await HandleResolver.ResolveToHandleAsync(opaqueHandle, client);

        Assert.Equal(opaqueHandle, result);
        Assert.Empty(handler.ListRequests);
    }

    [Fact]
    public async Task ResolveToHandleAsync_ReturnsOriginalId_WhenNotFound_AndDoesNotCache()
    {
        var handler = new ResolverRecordingHandler
        {
            ListResponses =
            {
                ["/api/people/"] = "[]"
            }
        };
        var client = CreateClient(handler);

        var first = await HandleResolver.ResolveToHandleAsync("I0099", client);
        var second = await HandleResolver.ResolveToHandleAsync("I0099", client);

        Assert.Equal("I0099", first);
        Assert.Equal("I0099", second);
        Assert.Equal(2, handler.ListRequestCount("/api/people/"));
    }

    [Fact]
    public async Task ResolveToHandleAsync_ReturnsOriginalId_OnApiError_AndDoesNotCache()
    {
        var handler = new ResolverRecordingHandler
        {
            ListStatusCodes =
            {
                ["/api/events/"] = HttpStatusCode.InternalServerError
            }
        };
        var client = CreateClient(handler);

        var first = await HandleResolver.ResolveToHandleAsync("E0005", client);
        var second = await HandleResolver.ResolveToHandleAsync("E0005", client);

        Assert.Equal("E0005", first);
        Assert.Equal("E0005", second);
        Assert.Equal(2, handler.ListRequestCount("/api/events/"));
    }

    [Fact]
    public async Task ResolveToHandleAsync_IsolatesCacheByTreeId()
    {
        var handler = new ResolverRecordingHandler
        {
            ListResponses =
            {
                ["/api/people/"] = """
                    [{"handle": "person-handle-tree-a", "gramps_id": "I0001"}]
                    """
            }
        };
        var clientA = CreateClient(handler, treeId: "tree-a");
        var clientB = CreateClient(handler, treeId: "tree-b");

        var handleA = await HandleResolver.ResolveToHandleAsync("I0001", clientA);
        var handleB = await HandleResolver.ResolveToHandleAsync("I0001", clientB);

        Assert.Equal("person-handle-tree-a", handleA);
        Assert.Equal("person-handle-tree-a", handleB);
        Assert.Equal(2, handler.ListRequestCount("/api/people/"));
    }

    [Fact]
    public async Task ResolveToHandleAsync_IsolatesCacheByApiUrl()
    {
        var handler = new ResolverRecordingHandler
        {
            ListResponses =
            {
                ["/api/people/"] = """
                    [{"handle": "person-handle-server", "gramps_id": "I0001"}]
                    """
            }
        };
        var clientA = CreateClient(handler, apiUrl: "https://gramps-a.test");
        var clientB = CreateClient(handler, apiUrl: "https://gramps-b.test");

        await HandleResolver.ResolveToHandleAsync("I0001", clientA);
        await HandleResolver.ResolveToHandleAsync("I0001", clientB);

        Assert.Equal(2, handler.ListRequestCount("/api/people/"));
    }

    private static GrampsApiClient CreateClient(
        ResolverRecordingHandler handler,
        string apiUrl = "https://gramps-web.test",
        string treeId = "tree")
    {
        var config = new GrampsConfig(
            ApiUrl: apiUrl,
            Username: "user",
            Password: "pass",
            TreeId: treeId);

        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(apiUrl)
        };

        return new GrampsApiClient(
            httpClient,
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private sealed class ResolverRecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public Dictionary<string, string> ListResponses { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, HttpStatusCode> ListStatusCodes { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<RecordedRequest> ListRequests
        {
            get
            {
                lock (_gate)
                {
                    return _requests
                        .Where(r => r.Method == HttpMethod.Get && r.Path.Contains("gramps_id=", StringComparison.Ordinal))
                        .ToList();
                }
            }
        }

        public int ListRequestCount(string pathPrefix)
        {
            lock (_gate)
            {
                return _requests.Count(r =>
                    r.Method == HttpMethod.Get &&
                    r.Path.StartsWith(pathPrefix, StringComparison.Ordinal) &&
                    r.Path.Contains("gramps_id=", StringComparison.Ordinal));
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            lock (_gate)
            {
                _requests.Add(new RecordedRequest(request.Method, path, body));
            }

            if (request.Method == HttpMethod.Post && path.StartsWith("/api/token/", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path.Contains("gramps_id=", StringComparison.Ordinal))
            {
                var listPath = path.Split('?')[0];
                if (ListStatusCodes.TryGetValue(listPath, out var statusCode))
                {
                    return new HttpResponseMessage(statusCode)
                    {
                        Content = new StringContent("error", Encoding.UTF8, "text/plain")
                    };
                }

                if (ListResponses.TryGetValue(listPath, out var json))
                    return JsonResponse(json);

                return JsonResponse("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain")
            };
        }

        private static HttpResponseMessage JsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);
}
