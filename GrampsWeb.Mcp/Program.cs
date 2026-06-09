using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Prompts;
using GrampsWeb.Mcp.Resources;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

try
{
    var config = GrampsConfig.FromEnvironment(args);
    var transport = McpTransportConfig.FromEnvironment();

    if (transport.Mode == McpListenMode.Stdio)
    {
        await RunStdioAsync(config);
    }
    else
    {
        await RunHttpAsync(args, config, transport);
    }
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Configuration validation failed"))
{
    await Console.Error.WriteLineAsync($"Configuration Error:\n{ex.Message}");
    Environment.Exit(1);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Fatal Error: {ex.Message}\n{ex.StackTrace}");
    Environment.Exit(1);
}

static async Task RunStdioAsync(GrampsConfig config)
{
    var builder = Host.CreateEmptyApplicationBuilder(settings: null);

    builder.Logging
        .ClearProviders()
        .AddSimpleConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.IncludeScopes = false;
        });

    builder.Services.AddSingleton(config);
    builder.Services.AddHttpClient<GrampsApiClient>();

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithResources<GrampsResources>()
        .WithPrompts<GrampsPrompts>();

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args, GrampsConfig config, McpTransportConfig transport)
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging
        .ClearProviders()
        .AddSimpleConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.IncludeScopes = false;
        });

    builder.Services.AddSingleton(config);
    builder.Services.AddHttpClient<GrampsApiClient>();

    builder.Services
        .AddMcpServer()
        .WithHttpTransport(options =>
        {
            options.Stateless = transport.Stateless;
            if (transport.EnableLegacySse)
            {
#pragma warning disable MCP9004 // EnableLegacySse: intentional for legacy SSE clients
                options.EnableLegacySse = true;
#pragma warning restore MCP9004
            }
        })
        .WithToolsFromAssembly()
        .WithResources<GrampsResources>()
        .WithPrompts<GrampsPrompts>();

    var app = builder.Build();
    app.MapMcp(transport.MapPath);
    await app.RunAsync();
}
