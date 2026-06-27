using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class GrampsApiClientReadOnlyTests
{
    [Fact]
    public async Task Mutation_Methods_Throw_Before_Sending_Http_Request()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, readOnly: true);

        var post = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PostMutationAsync("/api/people/", new { }, "Person"));
        var put = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PutMutationAsync("/api/people/handle1", new { }));
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DeleteAsync("/api/people/handle1"));

        Assert.Contains("Read-only mode is enabled", post.Message);
        Assert.Contains("Read-only mode is enabled", put.Message);
        Assert.Contains("Read-only mode is enabled", delete.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_Still_Authenticates_And_Reads_In_ReadOnly_Mode()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, readOnly: true);

        var result = await client.GetAsync<JsonElement>("/api/metadata/");

        Assert.Equal("tree", result.GetProperty("tree").GetString());
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/token/", request.Path);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/metadata/", request.Path);
            });
    }

    [Fact]
    public async Task GetBytesAsync_Still_Authenticates_And_Reads_In_ReadOnly_Mode()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, readOnly: true);

        var result = await client.GetBytesAsync("/api/media/handle1/file", maxBytes: 10);

        Assert.Equal([0, 159, 255], result.Bytes.ToArray());
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/api/token/", request.Path);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/media/handle1/file", request.Path);
            });
    }

    [Fact]
    public async Task GetBytesAsync_Rejects_Response_When_Content_Length_Exceeds_Limit()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, readOnly: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetBytesAsync("/api/media/large/file", maxBytes: 2));

        Assert.Contains("exceeding the configured limit", ex.Message);
        Assert.Contains("3 bytes", ex.Message);
    }

    [Fact]
    public async Task GetBytesAsync_Rejects_Streaming_Response_When_Bytes_Exceed_Limit()
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler, readOnly: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetBytesAsync("/api/media/streaming-large/file", maxBytes: 2));

        Assert.Contains("exceeding the configured limit", ex.Message);
        Assert.Contains("3 bytes", ex.Message);
    }

    [Fact]
    public async Task Concurrent_Client_Instances_Share_Token_Request()
    {
        var handler = new RecordingHandler
        {
            TokenDelay = TimeSpan.FromMilliseconds(50)
        };
        var config = CreateConfig(readOnly: true);
        var tokenProvider = CreateTokenProvider(handler, config);
        var client1 = CreateClient(handler, config, tokenProvider);
        var client2 = CreateClient(handler, config, tokenProvider);

        await Task.WhenAll(
            client1.GetAsync<JsonElement>("/api/metadata/"),
            client2.GetAsync<JsonElement>("/api/metadata/"));

        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && r.Path == "/api/token/"));
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Get && r.Path == "/api/metadata/"));
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler, bool readOnly)
    {
        var config = CreateConfig(readOnly);
        return CreateClient(handler, config, CreateTokenProvider(handler, config));
    }

    private static GrampsApiClient CreateClient(
        HttpMessageHandler handler,
        GrampsConfig config,
        GrampsAuthTokenProvider tokenProvider)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gramps-web.test")
        };

        return new GrampsApiClient(
            httpClient,
            config,
            NullLogger<GrampsApiClient>.Instance,
            tokenProvider);
    }

    private static GrampsAuthTokenProvider CreateTokenProvider(HttpMessageHandler handler, GrampsConfig config)
    {
        return new GrampsAuthTokenProvider(
            new HttpClient(handler),
            config,
            NullLogger<GrampsAuthTokenProvider>.Instance);
    }

    private static GrampsConfig CreateConfig(bool readOnly)
    {
        return new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree",
            ReadOnly: readOnly);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public TimeSpan TokenDelay { get; init; } = TimeSpan.Zero;

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            lock (_gate)
            {
                _requests.Add(new RecordedRequest(request.Method, path, body));
            }

            if (request.Method == HttpMethod.Post && path == "/api/token/")
            {
                if (TokenDelay > TimeSpan.Zero)
                    await Task.Delay(TokenDelay, cancellationToken);

                return JsonResponse("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/api/metadata/")
            {
                return JsonResponse("""
                    {
                      "tree": "tree"
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && path == "/api/media/handle1/file")
            {
                return BinaryResponse([0, 159, 255], "image/jpeg");
            }

            if (request.Method == HttpMethod.Get && path == "/api/media/large/file")
            {
                return BinaryResponse([1, 2, 3], "image/png");
            }

            if (request.Method == HttpMethod.Get && path == "/api/media/streaming-large/file")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new UnknownLengthContent([1, 2, 3], "image/png")
                };
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

        private static HttpResponseMessage BinaryResponse(byte[] bytes, string mimeType)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);

    private sealed class UnknownLengthContent(byte[] bytes, string mimeType) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            return stream.WriteAsync(bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
