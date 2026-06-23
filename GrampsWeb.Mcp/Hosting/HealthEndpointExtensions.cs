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

            var payload = new
            {
                status = status.IsHealthy ? "healthy" : "unhealthy",
                gramps_api_url = status.ApiUrl,
                gramps_tree_id = status.ConfiguredTreeId,
                tree_name = status.TreeName,
                tree_database_id = status.TreeDatabaseId,
                gramps_version = status.GrampsVersion,
                error = status.Error
            };

            return status.IsHealthy
                ? Results.Json(payload)
                : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
