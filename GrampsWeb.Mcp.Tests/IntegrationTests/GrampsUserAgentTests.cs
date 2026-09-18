using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Health;
using GrampsWeb.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

public class GrampsUserAgentTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData("1.2.3-rc.1+abcdef", "1.2.3-rc.1")]
    public void ProductVersion_PreservesReleaseAndPrerelease(string input, string expected)
    {
        Assert.Equal(expected, GrampsUserAgent.GetProductVersion(input));
    }

    [Fact]
    public async Task RegisteredClients_SendUserAgent_OnEveryRequest()
    {
        var requests = new List<(string Method, string Path, string UserAgent)>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrampsMcpCore(new GrampsConfig(
            ApiUrl: "https://gramps.example", Username: "user", Password: "secret", TreeId: "tree"));
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(requests)));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<GrampsApiClient>();
        await client.GetTokenAsync();
        await client.RefreshTokenAsync();
        await client.GetAsync<JsonElement>("/api/people/");
        await client.PostMutationAsync("/api/people/", new { }, "Person");
        await client.PutMutationAsync("/api/people/handle", new { });
        await client.DeleteAsync("/api/people/handle");
        await client.GetBytesAsync("/api/media/handle/file", 1024);
        var health = await provider.GetRequiredService<GrampsHealthService>().CheckAsync();
        Assert.True(health.IsHealthy);

        // A newly resolved typed client must also carry exactly one application identifier.
        await provider.GetRequiredService<GrampsApiClient>().GetAsync<JsonElement>("/api/people/");

        Assert.Equal(new[]
        {
            ("POST", "/api/token/"),
            ("POST", "/api/token/refresh/"),
            ("GET", "/api/people/"),
            ("POST", "/api/people/"),
            ("PUT", "/api/people/handle"),
            ("DELETE", "/api/people/handle"),
            ("GET", "/api/media/handle/file"),
            ("POST", "/api/token/"),
            ("GET", "/api/metadata/"),
            ("GET", "/api/people/")
        }, requests.Select(r => (r.Method, r.Path)));

        var version = typeof(GrampsApiClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Assert.All(requests, request => Assert.Equal(
            $"gramps-web-mcp/{version} (+https://github.com/Scormave/gramps-web-mcp)", request.UserAgent));
    }

    private sealed class RecordingHandler(List<(string Method, string Path, string UserAgent)> requests)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add((request.Method.Method, path, request.Headers.UserAgent.ToString()));
            // An immediately expired initial token forces the refresh request through its own path.
            var json = path switch
            {
                "/api/token/" => """{"access_token":"access","refresh_token":"refresh","expires_in":0}""",
                "/api/token/refresh/" => """{"access_token":"refreshed","expires_in":900}""",
                _ => "{}"
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
