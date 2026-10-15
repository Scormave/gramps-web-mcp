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
        McpAuthConfig auth,
        McpOriginConfig origins)
    {
        app.UseOriginValidation(origins);

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

    public static void LogRetiredEnvironmentVariables(this IHost host)
    {
        var retired = GrampsConfig.FindRetiredEnvironmentVariables();
        if (retired.Count == 0)
            return;

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GrampsWeb.Mcp.Config");

        foreach (var name in retired)
        {
            logger.LogWarning(
                "{Variable} is set but no longer used; remove it. Media access is controlled by GRAMPS_MEDIA_RESOURCES_ENABLED, GRAMPS_MEDIA_ALLOW_PRIVATE, and GRAMPS_MEDIA_MAX_BYTES.",
                name);
        }
    }

    /// <summary>
    /// Says why uploading is off when upload folders are set but media file access is not, as in the
    /// Claude Desktop extension, whose upload folder has a default while media access starts disabled.
    /// </summary>
    public static void LogUploadWithoutMediaAccess(this IHost host, GrampsConfig config)
    {
        if (config.MediaResourcesEnabled || config.MediaUploadDirs is not { Count: > 0 })
            return;

        host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GrampsWeb.Mcp.Config")
            .LogInformation(
                "Uploading is off: GRAMPS_MEDIA_UPLOAD_DIRS is set, but media file access is disabled. Set GRAMPS_MEDIA_RESOURCES_ENABLED=true to upload.");
    }
}
