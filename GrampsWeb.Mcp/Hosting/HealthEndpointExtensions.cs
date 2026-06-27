using GrampsWeb.Mcp.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsWeb.Mcp.Hosting;

internal static class HealthEndpointExtensions
{
    public static WebApplication MapHealthEndpoint(this WebApplication app)
    {
        app.MapGet("/health", async (GrampsHealthService healthService, CancellationToken cancellationToken) =>
        {
            var status = await healthService.CheckAsync(cancellationToken);
            var payload = CreateHealthPayload(status);

            return status.IsHealthy
                ? Results.Json(payload)
                : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }

    internal static object CreateHealthPayload(GrampsConnectivityStatus status)
    {
        return new
        {
            status = status.IsHealthy ? "healthy" : "unhealthy"
        };
    }
}
