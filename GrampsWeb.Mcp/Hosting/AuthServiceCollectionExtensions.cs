using GrampsWeb.Mcp.Auth;
using GrampsWeb.Mcp.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace GrampsWeb.Mcp.Hosting;

internal static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddGrampsMcpAuth(this IServiceCollection services, McpAuthConfig auth)
    {
        services.AddSingleton(auth);
        if (!auth.Enabled)
        {
            return services;
        }

        services.AddSingleton<ApiKeyValidator>();
        services
            .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.Scheme,
                displayName: null,
                configureOptions: _ => { });
        services.AddAuthorization();
        return services;
    }
}
