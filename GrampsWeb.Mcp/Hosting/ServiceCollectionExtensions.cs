using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Health;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsWeb.Mcp.Hosting;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrampsMcpCore(this IServiceCollection services, GrampsConfig config)
    {
        services.AddSingleton(config);
        services.AddHttpClient<GrampsApiClient>();
        services.AddHttpClient<GrampsHealthService>();
        return services;
    }

    public static IServiceCollection AddGrampsStartupCheck(
        this IServiceCollection services,
        McpTransportConfig? transport = null)
    {
        if (transport is not null)
        {
            services.AddSingleton(transport);
        }

        services.AddHostedService<GrampsStartupHostedService>();
        return services;
    }
}
