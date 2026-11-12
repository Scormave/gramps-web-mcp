using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsServerVersionTests
{
    [Theory]
    [InlineData("3.22.1", "3.22")]
    [InlineData("3.23.0rc1", "3.23")]
    [InlineData("v4.0", "4.0")]
    [InlineData("3", null)]
    [InlineData("dev", null)]
    public void Parse_ReadsMajorAndMinor(string version, string? expected)
    {
        Assert.Equal(expected is null ? null : Version.Parse(expected), GrampsServerVersion.Parse(version));
    }

    [Theory]
    [InlineData("3.16.0")]
    [InlineData("3.24.0")]
    [InlineData("4.0.0")]
    [InlineData("dev")]
    [InlineData(null)]
    public void UnsupportedMessage_IsNull_ForASupportedOrUnknownVersion(string? version)
    {
        Assert.Null(GrampsServerVersion.UnsupportedMessage(version));
    }

    [Fact]
    public void UnsupportedMessage_NamesBothVersions_AndTheWayOut()
    {
        var message = GrampsServerVersion.UnsupportedMessage("3.15.2");

        Assert.Equal(
            "This Gramps Web server runs Gramps Web API 3.15.2, and Gramps Web MCP needs 3.16 or newer. " +
            "Update Gramps Web (26.6.2 or later), or keep Gramps Web MCP 2.x until then.",
            message);
    }

    [Theory]
    [InlineData("""{"gramps_webapi": {"version": "3.24.0"}}""", "3.24.0")]
    [InlineData("""{"gramps_webapi": {}}""", null)]
    [InlineData("""{"gramps": {"version": "6.0.0"}}""", null)]
    [InlineData("""[]""", null)]
    public void ReadApiVersion_ReadsTheWebApiVersion(string metadata, string? expected)
    {
        using var doc = JsonDocument.Parse(metadata);

        Assert.Equal(expected, GrampsServerVersion.ReadApiVersion(doc.RootElement));
    }

    [Fact]
    public async Task ASupportedServer_IsReadOnce()
    {
        var check = new GrampsServerVersion();
        var reads = 0;

        for (var i = 0; i < 3; i++)
            await check.EnsureSupportedAsync(() => Read(ref reads, """{"gramps_webapi": {"version": "3.16.0"}}"""));

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AServerWithoutAVersion_IsLetThrough_AndReadOnce()
    {
        var check = new GrampsServerVersion();
        var reads = 0;

        await check.EnsureSupportedAsync(() => Read(ref reads, """{"database": {"name": "Tree"}}"""));
        await check.EnsureSupportedAsync(() => Read(ref reads, """{"database": {"name": "Tree"}}"""));

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AnOlderServer_IsRefusedOnEveryCall_AndPassesOnceUpgraded()
    {
        var check = new GrampsServerVersion();
        var reads = 0;

        for (var i = 0; i < 2; i++)
        {
            var ex = await Assert.ThrowsAsync<UnsupportedGrampsServerException>(
                () => check.EnsureSupportedAsync(() => Read(ref reads, """{"gramps_webapi": {"version": "3.15.2"}}""")));
            Assert.EndsWith("until then. Nothing was read or changed.", ex.Message);
        }

        await check.EnsureSupportedAsync(() => Read(ref reads, """{"gramps_webapi": {"version": "3.16.0"}}"""));

        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task AFailedRead_LetsTheRequestThrough_AndIsReadAgain()
    {
        var check = new GrampsServerVersion();
        var reads = 0;

        await check.EnsureSupportedAsync(() =>
        {
            reads++;
            throw new HttpRequestException("connection refused");
        });
        await check.EnsureSupportedAsync(() =>
        {
            reads++;
            throw new GrampsApiException(HttpStatusCode.ServiceUnavailable, "");
        });
        await Assert.ThrowsAsync<UnsupportedGrampsServerException>(
            () => check.EnsureSupportedAsync(() => Read(ref reads, """{"gramps_webapi": {"version": "3.15.2"}}""")));

        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneRead()
    {
        var check = new GrampsServerVersion();
        var reply = new TaskCompletionSource<string>();
        var reads = 0;

        var calls = Enumerable.Range(0, 4)
            .Select(_ => check.EnsureSupportedAsync(() =>
            {
                Interlocked.Increment(ref reads);
                return reply.Task;
            }))
            .ToList();
        reply.SetResult("""{"gramps_webapi": {"version": "3.24.0"}}""");
        await Task.WhenAll(calls);

        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Client_OnAnOlderServer_RefusesBeforeTheRequest()
    {
        var handler = new VersionServer("3.15.2");
        var client = CreateClient(handler, new GrampsServerVersion());

        var ex = await Assert.ThrowsAsync<UnsupportedGrampsServerException>(
            () => client.PutMutationAsync("/api/people/h1", new { }));

        Assert.Contains("runs Gramps Web API 3.15.2", ex.Message);
        Assert.Equal(["POST /api/token/", "GET /api/metadata/"], handler.Requests);
    }

    [Fact]
    public async Task Client_OnASupportedServer_ChecksOncePerProcess()
    {
        var handler = new VersionServer("3.24.0");
        var check = new GrampsServerVersion();

        await CreateClient(handler, check).GetAsync<JsonElement>("/api/people/h1");
        await CreateClient(handler, check).GetAsync<JsonElement>("/api/people/h2");

        Assert.Equal(
            ["GET /api/metadata/", "GET /api/people/h1", "GET /api/people/h2"],
            handler.Requests.Where(r => r != "POST /api/token/"));
    }

    [Fact]
    public async Task Client_WhenTheVersionReadFails_ReportsItsOwnRequest()
    {
        var handler = new VersionServer("3.24.0", metadataStatus: HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler, new GrampsServerVersion());

        await client.GetAsync<JsonElement>("/api/people/h1");
        await client.GetAsync<JsonElement>("/api/people/h2");

        Assert.Equal(
            ["GET /api/metadata/", "GET /api/people/h1", "GET /api/metadata/", "GET /api/people/h2"],
            handler.Requests.Where(r => r != "POST /api/token/"));
    }

    [Fact]
    public async Task Client_WithoutTheCheck_SendsOnlyItsOwnRequests()
    {
        var handler = new VersionServer("3.15.2");

        await CreateClient(handler, serverVersion: null).GetAsync<JsonElement>("/api/people/h1");

        Assert.Equal(["POST /api/token/", "GET /api/people/h1"], handler.Requests);
    }

    private static Task<string> Read(ref int reads, string metadata)
    {
        reads++;
        return Task.FromResult(metadata);
    }

    private static GrampsApiClient CreateClient(VersionServer handler, GrampsServerVersion? serverVersion)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler), config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri(config.ApiUrl) },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider,
            serverVersion: serverVersion);
    }

    /// <summary>
    /// Answers a token, metadata with <paramref name="apiVersion"/> or <paramref name="metadataStatus"/>, and an
    /// empty object for anything else.
    /// </summary>
    private sealed class VersionServer(string apiVersion, HttpStatusCode metadataStatus = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_gate)
                    return _requests.ToList();
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            lock (_gate)
                _requests.Add($"{request.Method} {path}");

            if (path == "/api/metadata/" && metadataStatus != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(metadataStatus)
                {
                    Content = new StringContent("""{"error": {"code": 503}}""", Encoding.UTF8, "application/json")
                });

            var body = path switch
            {
                "/api/token/" => """{"access_token": "token", "refresh_token": "refresh", "expires_in": 900}""",
                "/api/metadata/" => $$$"""{"gramps_webapi": {"version": "{{{apiVersion}}}"}}""",
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
