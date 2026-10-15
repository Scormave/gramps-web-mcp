using GrampsWeb.Mcp.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Hosting;

internal static class OriginValidationExtensions
{
    // The MCP spec allows a JSON-RPC error without an id in the 403 body.
    private const string ForbiddenBody =
        """{"jsonrpc":"2.0","error":{"code":-32000,"message":"Forbidden: this server does not accept requests from this browser origin"}}""";

    /// <summary>
    /// Refuses with 403 every request whose <c>Origin</c> header is not listed in <c>MCP_ALLOWED_ORIGINS</c>, as the
    /// MCP spec requires. A web page on another site, including one that DNS rebinding has made look same-origin,
    /// cannot then call tools with the server's Gramps credentials. Requests without the header pass.
    /// </summary>
    public static WebApplication UseOriginValidation(this WebApplication app, McpOriginConfig origins)
    {
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("GrampsWeb.Mcp.Auth");

        if (origins.AllowedOrigins.Count > 0)
        {
            logger.LogInformation(
                "Browser requests allowed from {Origins}",
                string.Join(", ", origins.AllowedOrigins.Order(StringComparer.Ordinal)));
        }

        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin;
            if (origin.Count == 0 || origins.IsAllowed(origin.ToString()))
            {
                await next(context);
                return;
            }

            logger.LogWarning(
                "Refused a request to {Path} from browser origin {Origin}; if the page is yours, list its origin in MCP_ALLOWED_ORIGINS",
                context.Request.Path,
                origin.ToString());
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(ForbiddenBody);
        });

        return app;
    }
}
