using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Hosting;
using GrampsWeb.Mcp.Logging;
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
    var auth = McpAuthConfig.FromEnvironment(transport.Mode);

    if (transport.Mode == McpListenMode.Stdio)
    {
        await RunStdioAsync(config, auth);
    }
    else
    {
        await RunHttpAsync(args, config, transport, auth);
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

static async Task RunStdioAsync(GrampsConfig config, McpAuthConfig auth)
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

    var host = builder.Build();

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MCP_API_KEY")))
    {
        host.LogIgnoredApiKeyInStdio();
    }

    await host.RunAsync();
}

static async Task RunHttpAsync(string[] args, GrampsConfig config, McpTransportConfig transport, McpAuthConfig auth)
{
    var builder = WebApplication.CreateBuilder(args);

    ConfigureLogging(builder.Logging);

    builder.Services
        .AddGrampsMcpCore(config)
        .AddGrampsStartupCheck(transport)
        .AddGrampsMcpAuth(auth);

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
    app.MapGrampsMcpEndpoints(transport, auth);
    app.LogAuthStartupStatus(auth);
    await app.RunAsync();
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging
        .ClearProviders()
        .SetMinimumLevel(LogLevel.Information)
        .AddFilter("GrampsWeb.Mcp", LogLevel.Information)
        .AddFilter("Microsoft", LogLevel.Warning)
        .AddFilter("System", LogLevel.Warning)
        .AddFilter("System.Net.Http.HttpClient.GrampsHealthService", LogLevel.Warning)
        .AddConsole(options => options.FormatterName = MinimalConsoleFormatter.FormatterName)
        .AddConsoleFormatter<MinimalConsoleFormatter, ConsoleFormatterOptions>();

    logging.Services.Configure<ConsoleLoggerOptions>(options =>
    {
        // Stdio MCP clients use stdout for the protocol; keep all logs on stderr.
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
}
