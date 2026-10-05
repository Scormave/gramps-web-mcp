using System.ComponentModel;
using System.Text;
using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace GrampsWeb.Mcp.Tests.IntegrationTests;

public class McpToolArgumentTests
{
    [Theory]
    [InlineData("""{"identifier":"I0001","extended":true}""", false, "probe I0001 True")]
    [InlineData("""{"identifier":"I0001","pagesize":"5"}""", false, "probe I0001 False 5")]
    [InlineData("""{"identifier":"I0001","date":1877}""", false, "probe I0001 False date 1877")]
    [InlineData("""{"identifier":1877}""", false, "probe 1877 False")]
    [InlineData("""{"id":"I0001"}""", true,
        "An error occurred invoking 'argument_probe': Unknown argument: id. Missing required argument: identifier. " +
        "Parameters: identifier (string, required), extended (boolean), pagesize (integer), date (string).")]
    [InlineData("""{"identifier":"I0001","extend":true}""", true,
        "An error occurred invoking 'argument_probe': Unknown argument: extend (did you mean extended?).")]
    [InlineData("""{"identifier":"I0001","extended":"yes"}""", true,
        "An error occurred invoking 'argument_probe': Argument extended must be a boolean, not the string")]
    [InlineData("""{"identifier":"I0001","extended":1}""", true,
        "An error occurred invoking 'argument_probe': Argument extended must be a boolean, not the number 1.")]
    public async Task McpPipeline_Explains_Wrong_Arguments(string arguments, bool isError, string text)
    {
        var (error, content) = await CallAsync("argument_probe", arguments);

        Assert.Equal(isError, error);
        Assert.StartsWith(text, content);
    }

    [Theory]
    [InlineData("""{"handle":"E0001","date":"1877"}""", false, "updated E0001 1877")]
    [InlineData("""{"handle":"E0001","linkMode":"add","isPrivate":false}""", false, "updated E0001  False")]
    [InlineData("""{"handle":"E0001"}""", true,
        "An error occurred invoking 'update_probe': Nothing to update: the call gives no field to change. " +
        "If you passed one, check its name; clients drop arguments a tool does not have. " +
        "Parameters: handle (string, required), date (string), isPrivate (boolean), linkMode (string).")]
    [InlineData("""{"handle":"E0001","linkMode":"add","date":null}""", true,
        "An error occurred invoking 'update_probe': Nothing to update")]
    public async Task McpPipeline_Refuses_An_Update_With_Nothing_To_Change(string arguments, bool isError, string text)
    {
        var (error, content) = await CallAsync("update_probe", arguments);

        Assert.Equal(isError, error);
        Assert.StartsWith(text, content);
    }

    private static async Task<(bool IsError, string? Text)> CallAsync(string tool, string arguments)
    {
        var config = new GrampsConfig("https://gramps.test", "user", "pass", "tree");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrampsMcpCore(config);
        builder.Services.AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithGrampsToolProfile(config)
            .WithTools<ProbeTools>();
        await using var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        using var http = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{arguments}}}}}""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var data = body.Split('\n').Single(line => line.StartsWith("data:", StringComparison.Ordinal))[5..];
        var result = JsonDocument.Parse(data).RootElement.GetProperty("result");
        await app.StopAsync();
        return (result.TryGetProperty("isError", out var error) && error.GetBoolean(),
            result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    public class ProbeTools
    {
        [McpServerTool(Name = "argument_probe", ReadOnly = true)]
        [Description("Echoes its arguments.")]
        public static string Probe(string identifier, bool extended = false, int? pagesize = null, string? date = null) =>
            $"probe {identifier} {extended} {pagesize}".TrimEnd() + (date == null ? "" : $" date {date}");

        [McpServerTool(Name = "update_probe")]
        [Description("Echoes an update.")]
        public static string UpdateProbe(string handle, string? date = null, bool? isPrivate = null, string linkMode = "replace") =>
            $"updated {handle} {date} {isPrivate}".TrimEnd();
    }
}
