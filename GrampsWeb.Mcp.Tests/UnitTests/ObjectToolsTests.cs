using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

[Collection("HandleCache")]
public class ObjectToolsTests
{
    [Fact]
    public async Task GetRecords_RejectsUnknownObjectType()
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.GetRecords(["handle"], "unknown"));

        Assert.Contains("Invalid objectType", ex.Message);
    }

    [Theory]
    [InlineData("event")]
    [InlineData("media")]
    [InlineData("tag")]
    public async Task GetRecords_RejectsExtendedWithoutAPersonOrFamily(string objectType)
    {
        var ex = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.GetRecords(["handle"], objectType, extended: true));

        Assert.Equal("extended is supported only for people and families, and identifiers names neither.", ex.Message);
    }

    [Fact]
    public async Task GetRecords_RequiresTypeForOpaqueHandle()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetRecords(["I0001", "opaque-handle"]));

        Assert.StartsWith("objectType is required for the handle 'opaque-handle'.", ex.Message);
    }

    [Fact]
    public async Task GetRecords_RejectsTypeThatConflictsWithGrampsIdPrefix()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetRecords(["I0001"], "event"));

        Assert.Equal("objectType 'event' does not match Gramps ID 'I0001', which identifies a person.", ex.Message);
    }

    [Theory]
    [InlineData(0, "identifiers must name at least one record.")]
    [InlineData(21, "identifiers names 21 records; one call reads at most 20.")]
    public async Task GetRecords_RejectsAnEmptyOrTooLongList(int count, string message)
    {
        var identifiers = Enumerable.Range(1, count).Select(i => $"N{i:0000}").ToArray();

        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetRecords(identifiers));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task GetRecords_RejectsAnEmptyEntry()
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => ObjectTools.GetRecords(["N0001", ""]));

        Assert.Equal("identifiers must not contain empty entries.", ex.Message);
    }

    [Fact]
    public async Task GetRecords_ReadsEachRecordOnceInTheOrderGiven()
    {
        HandleCache.Invalidate();
        using var http = new HttpClient(new EmptyTreeHandler());

        var text = await ObjectTools.GetRecords(["N0001", "I0002", " N0001 ", "E0003"], client: Client(http));

        Assert.Equal(
            ["Note not found: N0001", "Person not found: I0002", "Event not found: E0003"],
            text.Split("\n\n---\n\n").Select(record => record.Split('\n')[0]));
    }

    [Fact]
    public async Task GetRecords_ReadsOtherTypesNextToAnExtendedPerson()
    {
        HandleCache.Invalidate();
        using var http = new HttpClient(new EmptyTreeHandler());

        var text = await ObjectTools.GetRecords(["I0001", "E0001"], extended: true, client: Client(http));

        Assert.Equal(
            ["Person not found: I0001", "Event not found: E0001"],
            text.Split("\n\n---\n\n").Select(record => record.Split('\n')[0]));
    }

    [Theory]
    [InlineData("I0001", "people")]
    [InlineData("F0001", "families")]
    [InlineData("E0001", "events")]
    [InlineData("P0001", "places")]
    [InlineData("S0001", "sources")]
    [InlineData("C0001", "citations")]
    [InlineData("N0001", "notes")]
    [InlineData("O0001", "media")]
    [InlineData("R0001", "repositories")]
    public async Task DeleteRecords_ResolvesGrampsIdAndDeletesInItsCollection(string grampsId, string collection)
    {
        HandleCache.Invalidate();
        using var handler = new DeletionHandler(collection, grampsId);
        using var http = new HttpClient(handler);
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        var client = new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);

        var result = await ObjectTools.DeleteRecords([grampsId], client: client);

        Assert.Contains("action: deleted", result);
        Assert.Equal($$"""{"namespace":"{{collection}}","handles":["resolved-handle"]}""", handler.DeleteBody);
    }

    [Fact]
    public async Task DeleteRecords_RejectsUnknownObjectType()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.DeleteRecords(["handle"], "unknown", client: null!));

        Assert.Contains("Invalid objectType", error.Message);
    }

    [Fact]
    public async Task DeleteRecords_RequiresTypeForAHandle()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.DeleteRecords(["I0001", "opaque-handle"], client: null!));

        Assert.StartsWith("objectType is required for the handle 'opaque-handle'.", error.Message);
    }

    [Fact]
    public async Task DeleteRecords_RejectsTypeThatConflictsWithAGrampsIdPrefix()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.DeleteRecords(["N0001", "I0001"], "note", client: null!));

        Assert.Equal("objectType 'note' does not match Gramps ID 'I0001', which identifies a person.", error.Message);
    }

    [Theory]
    [InlineData(0, "identifiers must name at least one record.")]
    [InlineData(51, "identifiers names 51 records; one call deletes at most 50.")]
    public async Task DeleteRecords_RejectsAnEmptyOrTooLongList(int count, string message)
    {
        var identifiers = Enumerable.Range(1, count).Select(i => $"N{i:0000}").ToArray();

        var error = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.DeleteRecords(identifiers, client: null!));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task DeleteRecords_RejectsAnEmptyEntry()
    {
        var error = await Assert.ThrowsAsync<McpException>(
            () => ObjectTools.DeleteRecords(["N0001", " "], client: null!));

        Assert.Equal("identifiers must not contain empty entries.", error.Message);
    }

    private static GrampsApiClient Client(HttpClient http)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>A tree without records: every Gramps ID lookup finds nothing and every record is missing.</summary>
    private sealed class EmptyTreeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""));
            Assert.Equal(HttpMethod.Get, request.Method);
            if (path.Contains("?gramps_id=", StringComparison.Ordinal))
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, "[]"));
            return Task.FromResult(JsonResponse(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}"""));
        }
    }

    private sealed class DeletionHandler(string collection, string grampsId) : HttpMessageHandler
    {
        public string? DeleteBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return JsonResponse(HttpStatusCode.OK, """{"access_token":"token","refresh_token":"refresh","expires_in":900}""");
            if (path == $"/api/{collection}/?gramps_id={grampsId}&pagesize=1")
                return JsonResponse(HttpStatusCode.OK, """[{"handle":"resolved-handle"}]""");
            if (path == $"/api/{collection}/resolved-handle?keys=handle,gramps_id,change,backlinks&backlinks=true")
                return JsonResponse(HttpStatusCode.OK, $$"""{"handle":"resolved-handle","gramps_id":"{{grampsId}}","backlinks":{} }""");
            if (request.Method == HttpMethod.Post && path == "/api/objects/delete-by-handle/")
            {
                DeleteBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            return JsonResponse(HttpStatusCode.NotFound, """{"error": {"code": 404, "message": "Not found"}}""");
        }

    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
