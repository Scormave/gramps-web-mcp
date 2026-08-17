using System.Net;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsApiClientMutationPolicyTests
{
    [Fact]
    public async Task Concurrent_Mutations_On_Shared_Gate_Do_Not_Overlap_Http()
    {
        var handler = new RecordingHandler { MutationDelay = TimeSpan.FromMilliseconds(60) };
        var gate = new MutationGate(serialize: true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var client1 = CreateClient(handler, gate);
        var client2 = CreateClient(handler, gate);

        await Task.WhenAll(
            client1.PostMutationAsync("/api/people/", new { }, "Person"),
            client2.PostMutationAsync("/api/people/", new { }, "Person"));

        var peoplePosts = handler.Spans
            .Where(s => s.Method == HttpMethod.Post && s.Path == "/api/people/")
            .OrderBy(s => s.StartedAt)
            .ToList();

        Assert.Equal(2, peoplePosts.Count);
        Assert.True(peoplePosts[1].StartedAt >= peoplePosts[0].EndedAt);
    }

    [Fact]
    public async Task GetAsync_Does_Not_Wait_For_Mutation_Gate()
    {
        var handler = new RecordingHandler { MutationDelay = TimeSpan.FromMilliseconds(250) };
        var gate = new MutationGate(serialize: true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var writer = CreateClient(handler, gate);
        var reader = CreateClient(handler, gate);

        var write = writer.PostMutationAsync("/api/people/", new { }, "Person");
        await Task.Delay(20);
        var started = DateTime.UtcNow;
        await reader.GetAsync<System.Text.Json.JsonElement>("/api/metadata/");
        var elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;

        Assert.True(elapsedMs < 150, $"GET waited {elapsedMs}ms for the mutation gate");
        await write;
    }

    [Fact]
    public async Task MinInterval_Is_Applied_Between_Sequential_Mutations()
    {
        var handler = new RecordingHandler();
        var gate = new MutationGate(
            serialize: true,
            minInterval: TimeSpan.FromMilliseconds(80),
            acquireTimeout: TimeSpan.FromSeconds(5));
        var client = CreateClient(handler, gate);

        await client.PostMutationAsync("/api/people/", new { }, "Person");
        await client.PostMutationAsync("/api/people/", new { }, "Person");

        var peoplePosts = handler.Spans
            .Where(s => s.Method == HttpMethod.Post && s.Path == "/api/people/")
            .OrderBy(s => s.StartedAt)
            .ToList();

        Assert.Equal(2, peoplePosts.Count);
        Assert.True((peoplePosts[1].StartedAt - peoplePosts[0].EndedAt).TotalMilliseconds >= 60);
    }

    [Fact]
    public async Task ReadOnly_Throws_Before_Http_And_Before_Waiting_On_Gate()
    {
        var handler = new RecordingHandler();
        var held = new TaskCompletionSource();
        var gate = new MutationGate(serialize: true, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        var writable = CreateClient(handler, gate, readOnly: false);
        var readOnly = CreateClient(handler, gate, readOnly: true);

        var blocking = gate.RunAsync(() => held.Task);
        var started = DateTime.UtcNow;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => readOnly.PostMutationAsync("/api/people/", new { }, "Person"));
        var elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;

        Assert.Contains("Read-only mode is enabled", ex.Message);
        Assert.Empty(handler.Spans.Where(s => s.Path == "/api/people/"));
        Assert.True(elapsedMs < 100, $"read-only waited {elapsedMs}ms on the mutation gate");

        held.SetResult();
        await blocking;
        await writable.PostMutationAsync("/api/people/", new { }, "Person");
    }

    [Fact]
    public async Task Sqlite_Lock_500_Is_Rewritten()
    {
        var handler = new RecordingHandler { MutationStatus = HttpStatusCode.InternalServerError, MutationBody = "sqlite3.OperationalError: database is locked" };
        var client = CreateClient(handler, MutationGate.Disabled);

        var ex = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.PostMutationAsync("/api/people/", new { }, "Person"));

        Assert.Contains("database is locked", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry after", ex.Message);
        Assert.DoesNotContain("Gramps API error 500", ex.Message);
    }

    [Fact]
    public async Task Generic_500_Keeps_Original_Message()
    {
        var handler = new RecordingHandler { MutationStatus = HttpStatusCode.InternalServerError, MutationBody = "internal error" };
        var client = CreateClient(handler, MutationGate.Disabled);

        var ex = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.PutMutationAsync("/api/people/h1", new { }));

        Assert.StartsWith("Gramps API error 500", ex.Message);
        Assert.Contains("internal error", ex.Message);
    }

    [Fact]
    public async Task RateLimit_429_Is_Rewritten()
    {
        var handler = new RecordingHandler { MutationStatus = HttpStatusCode.TooManyRequests, MutationBody = "slow down" };
        var client = CreateClient(handler, MutationGate.Disabled);

        var ex = await Assert.ThrowsAsync<GrampsApiException>(
            () => client.DeleteAsync("/api/people/h1"));

        Assert.Contains("HTTP 429", ex.Message);
        Assert.Contains("Retry after", ex.Message);
    }

    [Fact]
    public async Task Gate_Timeout_Surfaces_Retryable_Error_Without_Sending_Mutation()
    {
        var handler = new RecordingHandler { MutationDelay = TimeSpan.FromMilliseconds(200) };
        var gate = new MutationGate(serialize: true, TimeSpan.Zero, TimeSpan.FromMilliseconds(40));
        var client1 = CreateClient(handler, gate);
        var client2 = CreateClient(handler, gate);

        var first = client1.PostMutationAsync("/api/people/", new { }, "Person");
        await Task.Delay(15);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client2.PostMutationAsync("/api/people/", new { }, "Person"));

        Assert.Contains("queue timed out", ex.Message);
        Assert.Contains("Retry after", ex.Message);
        await first;
        Assert.Equal(1, handler.Spans.Count(s => s.Path == "/api/people/"));
    }

    private static GrampsApiClient CreateClient(
        HttpMessageHandler handler,
        MutationGate gate,
        bool readOnly = false)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree",
            ReadOnly: readOnly);
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);

        return new GrampsApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://gramps-web.test") },
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider,
            gate);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RequestSpan> _spans = [];

        public TimeSpan MutationDelay { get; init; } = TimeSpan.Zero;
        public HttpStatusCode MutationStatus { get; init; } = HttpStatusCode.OK;
        public string MutationBody { get; init; } =
            """[{"_class":"Person","new":{"handle":"h1","gramps_id":"I1"}}]""";

        public IReadOnlyList<RequestSpan> Spans
        {
            get
            {
                lock (_gate)
                    return _spans.ToList();
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var started = DateTimeOffset.UtcNow;

            if (request.Method == HttpMethod.Post && path == "/api/token/")
            {
                Record(request.Method, path, started, DateTimeOffset.UtcNow);
                return Json("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/api/metadata/")
            {
                Record(request.Method, path, started, DateTimeOffset.UtcNow);
                return Json("""{ "tree": "tree" }""");
            }

            if (MutationDelay > TimeSpan.Zero)
                await Task.Delay(MutationDelay, cancellationToken);

            var ended = DateTimeOffset.UtcNow;
            Record(request.Method, path, started, ended);

            var response = new HttpResponseMessage(MutationStatus)
            {
                Content = new StringContent(MutationBody, Encoding.UTF8, "application/json")
            };
            if (MutationStatus == HttpStatusCode.TooManyRequests)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return response;
        }

        private void Record(HttpMethod method, string path, DateTimeOffset started, DateTimeOffset ended)
        {
            lock (_gate)
                _spans.Add(new RequestSpan(method, path, started, ended));
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed record RequestSpan(
        HttpMethod Method,
        string Path,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt);
}
