using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Health;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsWeb.Mcp.Hosting;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGrampsMcpCore(this IServiceCollection services, GrampsConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(new MutationGate(config));
        services.AddHttpClient(nameof(GrampsAuthTokenProvider), GrampsUserAgent.Configure);
        services.AddSingleton(sp =>
            new GrampsAuthTokenProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GrampsAuthTokenProvider)),
                config,
                sp.GetRequiredService<ILogger<GrampsAuthTokenProvider>>()));
        services.AddHttpClient<GrampsApiClient>(GrampsUserAgent.Configure);
        services.AddHttpClient<GrampsHealthService>(GrampsUserAgent.Configure);
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
