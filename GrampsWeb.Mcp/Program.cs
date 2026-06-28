using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Prompts;
using GrampsWeb.Mcp.Resources;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

try
{
    var config = GrampsConfig.FromEnvironment();
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

    ConfigureLogging(builder.Logging);

    builder.Services
        .AddGrampsMcpCore(config);

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

    ConfigureLogging(builder.Logging);

    builder.Services
        .AddGrampsMcpCore(config)
        .AddGrampsStartupCheck(transport);

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
    app.MapHealthEndpoint();
    app.MapMcp(transport.MapPath);
    await app.RunAsync();
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging
        .ClearProviders()
        .AddFilter("System.Net.Http.HttpClient.GrampsHealthService", LogLevel.Warning)
        .AddSimpleConsole(options =>
        {
            options.UseUtcTimestamp = true;
            options.IncludeScopes = false;
        });

    logging.Services.Configure<ConsoleLoggerOptions>(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
}
