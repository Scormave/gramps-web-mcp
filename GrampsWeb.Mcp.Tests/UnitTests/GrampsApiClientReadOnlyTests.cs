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

    private static GrampsApiClient CreateClient(HttpMessageHandler handler, bool readOnly)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gramps-web.test")
        };
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree",
            ReadOnly: readOnly);

        return new GrampsApiClient(httpClient, config, NullLogger<GrampsApiClient>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                body));

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/token/")
            {
                return JsonResponse("""
                    {
                      "access_token": "token",
                      "refresh_token": "refresh",
                      "expires_in": 900
                    }
                    """);
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/api/metadata/")
            {
                return JsonResponse("""
                    {
                      "tree": "tree"
                    }
                    """);
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
