using System.Net;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

public class McpReadCacheTests
{
    [Theory]
    [InlineData("read_probe", 1)]
    [InlineData("write_probe", 2)]
    public async Task McpPipeline_CachesOnlyReadToolsAndStartsFreshOnEveryCall(string tool, int readsPerCall)
    {
        using var handler = new Handler();
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrampsMcpCore(config);
        builder.Services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithGrampsToolProfile(config)
            .WithTools<ProbeTools>();
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        using var http = app.GetTestClient();

        for (var i = 1; i <= 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0", id = i, method = "tools/call",
                    @params = new { name = tool, arguments = new { } }
                }), Encoding.UTF8, "application/json")
            };
            request.Headers.Accept.ParseAdd("application/json, text/event-stream");
            using var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            Assert.Contains("probe-ok", await response.Content.ReadAsStringAsync());
            Assert.Equal(i * readsPerCall, handler.Reads);
        }
        await app.StopAsync();
    }

    public class ProbeTools
    {
        [McpServerTool(ReadOnly = true)]
        public static Task<string> ReadProbe(GrampsApiClient client) => Probe(client);

        [McpServerTool(ReadOnly = false)]
        public static Task<string> WriteProbe(GrampsApiClient client) => Probe(client);

        private static async Task<string> Probe(GrampsApiClient client)
        {
            await client.GetAsync<JsonElement>("/api/people/p1");
            await client.GetAsync<JsonElement>("/api/people/p1");
            return "probe-ok";
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.RequestUri!.AbsolutePath.StartsWith("/api/token/");
            if (!token) Interlocked.Increment(ref Reads);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(token
                    ? """{"access_token":"token","refresh_token":"refresh","expires_in":900}"""
                    : "{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
