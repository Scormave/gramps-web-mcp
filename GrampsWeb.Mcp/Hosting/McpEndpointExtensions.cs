using GrampsWeb.Mcp.Config;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Hosting;

internal static class McpEndpointExtensions
{
    public static WebApplication MapGrampsMcpEndpoints(
        this WebApplication app,
        McpTransportConfig transport,
        McpAuthConfig auth)
    {
        if (auth.Enabled)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.MapHealthEndpoint();

        if (auth.Enabled)
        {
            var group = app.MapGroup(transport.MapPath);
            group.RequireAuthorization();
            group.MapMcp(string.Empty);
        }
        else
        {
            app.MapMcp(transport.MapPath);
        }

        return app;
    }

    public static void LogAuthStartupStatus(this WebApplication app, McpAuthConfig auth)
    {
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GrampsWeb.Mcp.Auth");

        if (auth.Enabled)
        {
            logger.LogInformation("API key auth enabled ({KeyCount} key(s))", auth.ApiKeys.Length);
            return;
        }

        if (auth.WarnAboutAnonymousAccess)
        {
            logger.LogWarning(
                """
                MCP HTTP endpoints are reachable without authentication.
                Anyone who can reach this port can invoke tools using the configured Gramps credentials.
                Set MCP_API_KEY to a secret of at least {MinKeyLength} characters (generate one with: openssl rand -base64 32).
                Alternatively, terminate authentication at a reverse proxy or bind to loopback only (127.0.0.1).
                In Docker, ASPNETCORE_URLS is typically 0.0.0.0 even when the host port is published on 127.0.0.1 only — this warning is expected if external access is already restricted.
                """,
                McpAuthConfig.MinKeyLength);
        }
    }

    public static void LogIgnoredApiKeyInStdio(this IHost host)
    {
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GrampsWeb.Mcp.Auth");

        logger.LogWarning(
            "MCP_API_KEY is set but ignored in stdio transport; API key auth applies only to HTTP/SSE mode.");
    }
}
