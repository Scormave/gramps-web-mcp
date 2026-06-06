using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

public class CitationToolsTests
{
    [Theory]
    [InlineData("5")]
    [InlineData("10")]
    [InlineData("123")]
    [InlineData("p. 5")]
    [InlineData("vol. 3")]
    public async Task CreateCitation_Sends_Page_As_String(string page)
    {
        var handler = new RecordingHandler();
        var client = CreateClient(handler);

        var result = await CitationTools.CreateCitation(
            sourceHandle: "source1",
            page: new FlexibleString { Value = page },
            client: client);

        var citationRequest = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == "/api/citations/");
        using var body = JsonDocument.Parse(citationRequest.Body!);
        var pageProperty = body.RootElement.GetProperty("page");

        Assert.Equal(JsonValueKind.String, pageProperty.ValueKind);
        Assert.Equal(page, pageProperty.GetString());
        Assert.Contains($"name: {JsonSerializer.Serialize(page)}", result);
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://gramps-web.test")
        };
        var config = new GrampsConfig(
            ApiUrl: "https://gramps-web.test",
            Username: "user",
            Password: "pass",
            TreeId: "tree");

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

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/api/citations/")
            {
                return JsonResponse("""
                    [
                      {
                        "_class": "Citation",
                        "type": "add",
                        "old": null,
                        "new": {
                          "_class": "Citation",
                          "handle": "citation1",
                          "gramps_id": "C0001",
                          "source_handle": "source1",
                          "page": "5"
                        }
                      }
                    ]
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
