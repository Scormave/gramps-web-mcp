using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class ObjectToolsTests
{
    [Fact]
    public async Task GetObject_RejectsUnknownObjectType()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.GetObject("handle", "unknown"));

        Assert.Contains("Invalid objectType", ex.Message);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("media")]
    [InlineData("tag")]
    public async Task GetObject_RejectsExtendedForUnsupportedTypes(string objectType)
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.GetObject("handle", objectType, extended: true));

        Assert.Equal("extended is supported only for objectType person or family.", ex.Message);
    }

    [Fact]
    public async Task GetObject_RequiresTypeForOpaqueHandle()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetObject("opaque-handle"));

        Assert.Contains("objectType is required", ex.Message);
    }

    [Fact]
    public async Task GetObject_RejectsTypeThatConflictsWithGrampsIdPrefix()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetObject("I0001", "event"));

        Assert.Equal("objectType 'event' does not match Gramps ID 'I0001', which identifies a person.", ex.Message);
    }

    [Theory]
    [InlineData("person", "I0001", "people")]
    [InlineData("family", "F0001", "families")]
    [InlineData("event", "E0001", "events")]
    [InlineData("place", "P0001", "places")]
    [InlineData("source", "S0001", "sources")]
    [InlineData("citation", "C0001", "citations")]
    [InlineData("note", "N0001", "notes")]
    [InlineData("media", "M0001", "media")]
    [InlineData("repository", "R0001", "repositories")]
    [InlineData("tag", "T0001", "tags")]
    public async Task DeleteObject_ResolvesGrampsIdAndUsesCorrectCollection(
        string objectType, string grampsId, string collection)
    {
        using var handler = new DeletionHandler(collection, grampsId);
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = await ObjectTools.DeleteObject(objectType, grampsId, client: client);

        Assert.Contains("action: deleted", result);
        Assert.Equal($"/api/{collection}/resolved-handle", handler.DeletePath);
    }

    [Fact]
    public async Task DeleteObject_RejectsUnknownObjectType()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => ObjectTools.DeleteObject("unknown", "handle", client: null!));

        Assert.Contains("Invalid objectType", error.Message);
    }

    private sealed class DeletionHandler(string collection, string grampsId) : HttpMessageHandler
    {
        public string? DeletePath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse("""{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            if (path == $"/api/{collection}/?gramps_id={grampsId}&pagesize=1")
                return Task.FromResult(JsonResponse("""[{"handle":"resolved-handle"}]"""));
            if (path == $"/api/{collection}/resolved-handle?backlinks=true")
                return Task.FromResult(JsonResponse("""{"handle":"resolved-handle","backlinks":{}}"""));

            Assert.Equal(HttpMethod.Delete, request.Method);
            DeletePath = path;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
