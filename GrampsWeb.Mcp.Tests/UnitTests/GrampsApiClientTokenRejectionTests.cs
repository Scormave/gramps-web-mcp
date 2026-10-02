using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsApiClientTokenRejectionTests
{
    private const string SignatureRejected = """{"msg":"Signature verification failed"}""";

    [Fact]
    public async Task GetAsync_Logs_In_Again_And_Retries_When_The_Token_Signature_Is_Rejected()
    {
        // After the server's SECRET_KEY changed, neither the access nor the refresh token verifies.
        var handler = new TokenHandler { RejectRefresh = true };
        var client = CreateClient(handler);

        var person = await client.GetAsync<JsonElement>("/api/people/h1");

        Assert.Equal("h1", person.GetProperty("handle").GetString());
        Assert.Equal(
            ["/api/token/", "/api/people/h1 old", "/api/token/refresh/", "/api/token/", "/api/people/h1 new"],
            handler.Requests.Select(r => r.Describe()));
    }

    [Fact]
    public async Task GetAsync_Refreshes_And_Retries_When_The_Token_Has_Expired()
    {
        var handler = new TokenHandler
        {
            RejectionStatus = HttpStatusCode.Unauthorized,
            RejectionBody = """{"msg":"Token has expired"}"""
        };
        var client = CreateClient(handler);

        await client.GetAsync<JsonElement>("/api/people/h1");

        Assert.Equal(
            ["/api/token/", "/api/people/h1 old", "/api/token/refresh/", "/api/people/h1 refreshed"],
            handler.Requests.Select(r => r.Describe()));
    }

    [Fact]
    public async Task PostMutationAsync_Resends_The_Body_After_Replacing_A_Rejected_Token()
    {
        var handler = new TokenHandler { RejectRefresh = true };
        var client = CreateClient(handler);

        var (handle, grampsId) = await client.PostMutationAsync("/api/people/", new { gramps_id = "I1" }, "Person");

        Assert.Equal(("h1", "I1"), (handle, grampsId));
        var posts = handler.Requests.Where(r => r.Path == "/api/people/").ToList();
        Assert.Equal(["old", "new"], posts.Select(r => r.Token));
        Assert.All(posts, post => Assert.Equal("""{"gramps_id":"I1"}""", post.Body));
    }

    [Fact]
    public async Task GetBytesAsync_Retries_With_A_Replaced_Token()
    {
        var handler = new TokenHandler();
        var client = CreateClient(handler);

        var media = await client.GetBytesAsync("/api/media/m1/file", maxBytes: 16);

        Assert.Equal("bytes"u8.ToArray(), media.Bytes);
        Assert.Equal(["old", "refreshed"], handler.Requests.Where(r => r.Path == "/api/media/m1/file").Select(r => r.Token));
    }

    [Fact]
    public async Task GetAsync_Does_Not_Retry_A_Validation_Error()
    {
        var handler = new TokenHandler
        {
            RejectionBody = """{"error":{"code":422,"message":"Unprocessable Content"}}"""
        };
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<GrampsApiException>(() => client.GetAsync<JsonElement>("/api/people/h1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Equal(["/api/token/", "/api/people/h1 old"], handler.Requests.Select(r => r.Describe()));
    }

    [Fact]
    public async Task GetAsync_Retries_Once_When_The_Replaced_Token_Is_Rejected_Too()
    {
        var handler = new TokenHandler { RejectedTokens = ["old", "refreshed"] };
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<GrampsApiException>(() => client.GetAsync<JsonElement>("/api/people/h1"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, ex.StatusCode);
        Assert.Equal(2, handler.Requests.Count(r => r.Path == "/api/people/h1"));
    }

    [Fact]
    public async Task Requests_Rejected_Together_Replace_The_Token_Once()
    {
        var handler = new TokenHandler { RejectionDelay = TimeSpan.FromMilliseconds(100) };
        var client = CreateClient(handler);
        await client.EnsureAuthenticatedAsync();

        await Task.WhenAll(
            client.GetAsync<JsonElement>("/api/people/h1"),
            client.GetAsync<JsonElement>("/api/people/h2"));

        Assert.Single(handler.Requests, r => r.Path == "/api/token/");
        Assert.Single(handler.Requests, r => r.Path == "/api/token/refresh/");
        Assert.Equal(4, handler.Requests.Count(r => r.Path.StartsWith("/api/people/", StringComparison.Ordinal)));
    }

    private static GrampsApiClient CreateClient(TokenHandler handler)
    {
        var config = new GrampsConfig("https://gramps-web.test", "user", "pass", "tree");
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler), config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(
            new HttpClient(handler), config, NullLogger<GrampsApiClient>.Instance, tokenProvider);
    }

    /// <summary>
    /// Logs in with "old" first and "new" afterwards, refreshes to "refreshed" unless
    /// <see cref="RejectRefresh"/>, and rejects API requests sent with <see cref="RejectedTokens"/>.
    /// </summary>
    private sealed class TokenHandler : HttpMessageHandler
    {
        private int _logins;

        public HashSet<string> RejectedTokens { get; init; } = ["old"];
        public HttpStatusCode RejectionStatus { get; init; } = HttpStatusCode.UnprocessableEntity;
        public string RejectionBody { get; init; } = SignatureRejected;
        public TimeSpan RejectionDelay { get; init; } = TimeSpan.Zero;
        public bool RejectRefresh { get; init; }

        private readonly ConcurrentQueue<RecordedRequest> _requests = new();
        public IReadOnlyList<RecordedRequest> Requests => _requests.ToList();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var token = request.Headers.Authorization?.Parameter;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (path == "/api/token/")
            {
                _requests.Enqueue(new RecordedRequest(path, null, null));
                var access = Interlocked.Increment(ref _logins) == 1 ? "old" : "new";
                return Json(HttpStatusCode.OK, $$"""{"access_token":"{{access}}","refresh_token":"refresh","expires_in":900}""");
            }

            if (path == "/api/token/refresh/")
            {
                _requests.Enqueue(new RecordedRequest(path, null, null));
                return RejectRefresh
                    ? Json(HttpStatusCode.UnprocessableEntity, SignatureRejected)
                    : Json(HttpStatusCode.OK, """{"access_token":"refreshed","expires_in":900}""");
            }

            _requests.Enqueue(new RecordedRequest(path, token, body));
            if (token is not null && RejectedTokens.Contains(token))
            {
                await Task.Delay(RejectionDelay, cancellationToken);
                return Json(RejectionStatus, RejectionBody);
            }

            if (path.StartsWith("/api/media/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("bytes"u8.ToArray()) };

            return request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, """[{"_class":"Person","new":{"handle":"h1","gramps_id":"I1"}}]""")
                : Json(HttpStatusCode.OK, $$"""{"handle":"{{path.Split('/')[^1]}}"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
            new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed record RecordedRequest(string Path, string? Token, string? Body)
    {
        public string Describe() => Token is null ? Path : $"{Path} {Token}";
    }
}
