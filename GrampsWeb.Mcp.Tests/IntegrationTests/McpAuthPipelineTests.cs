using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

public class McpAuthPipelineTests
{
    private const string ApiKey = "0123456789abcdef";
    private const string MapPath = "/mcp";
    private const string MinimalJsonRpcBody =
        """{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}},"id":1}""";

    [Fact]
    public async Task Post_Mcp_Without_Key_Returns_401_With_WwwAuthenticate()
    {
        await using var factory = await CreateFactoryAsync(enabled: true, enableLegacySse: false);
        using var client = factory.CreateClient();

        using var request = CreateMcpPostRequest(MapPath, "{}");
        var response = await client.SendAsync(request);

        Assert.Equal(401, (int)response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));
    }

    [Fact]
    public async Task Post_Mcp_With_Bearer_Key_Does_Not_Return_401()
    {
        await using var factory = await CreateFactoryAsync(enabled: true, enableLegacySse: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

        using var request = CreateMcpPostRequest(MapPath, MinimalJsonRpcBody);
        var response = await client.SendAsync(request);

        Assert.NotEqual(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Post_Mcp_With_X_Api_Key_Does_Not_Return_401()
    {
        await using var factory = await CreateFactoryAsync(enabled: true, enableLegacySse: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);

        using var request = CreateMcpPostRequest(MapPath, MinimalJsonRpcBody);
        var response = await client.SendAsync(request);

        Assert.NotEqual(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_Without_Key_Does_Not_Return_401()
    {
        await using var factory = await CreateFactoryAsync(enabled: true, enableLegacySse: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.NotEqual(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Get_Legacy_Sse_Without_Key_Returns_401_When_Auth_Enabled()
    {
        await using var factory = await CreateFactoryAsync(enabled: true, enableLegacySse: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{MapPath}/sse");

        Assert.Equal(401, (int)response.StatusCode);
    }

    [Fact]
    public async Task Post_Mcp_Without_Key_Allowed_When_Auth_Disabled()
    {
        await using var factory = await CreateFactoryAsync(enabled: false, enableLegacySse: false);
        using var client = factory.CreateClient();

        using var request = CreateMcpPostRequest(MapPath, MinimalJsonRpcBody);
        var response = await client.SendAsync(request);

        Assert.NotEqual(401, (int)response.StatusCode);
    }

    private static HttpRequestMessage CreateMcpPostRequest(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        return request;
    }

    private static async Task<TestServerFactory> CreateFactoryAsync(bool enabled, bool enableLegacySse)
    {
        var auth = enabled
            ? new McpAuthConfig([ApiKey], WarnAboutAnonymousAccess: false)
            : new McpAuthConfig([], WarnAboutAnonymousAccess: true);

        var transport = new McpTransportConfig(
            Mode: McpListenMode.Http,
            MapPath: MapPath,
            Stateless: !enableLegacySse,
            EnableLegacySse: enableLegacySse);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddGrampsMcpAuth(auth);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = transport.Stateless;
                if (transport.EnableLegacySse)
                {
#pragma warning disable MCP9004
                    options.EnableLegacySse = true;
#pragma warning restore MCP9004
                }
            });

        RegisterHealthDependencies(builder.Services);

        var app = builder.Build();
        app.MapGrampsMcpEndpoints(transport, auth);

        await app.StartAsync();
        return new TestServerFactory(app);
    }

    private static void RegisterHealthDependencies(IServiceCollection services)
    {
        var config = new GrampsConfig(
            ApiUrl: "https://example.test",
            Username: "user",
            Password: "password",
            TreeId: "tree-id");

        services.AddSingleton(config);
        services.AddHttpClient<GrampsWeb.Mcp.Health.GrampsHealthService>()
            .ConfigurePrimaryHttpMessageHandler(() => new UnreachableHandler());
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    private sealed class TestServerFactory(WebApplication app) : IAsyncDisposable
    {
        public HttpClient CreateClient() => app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
