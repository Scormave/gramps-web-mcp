using System;
using System.Net.Http;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Tests;

/// <summary>
/// Helper for creating test instances of GrampsApiClient and configuration.
/// Used by all integration tests.
/// </summary>
public static class TestSetup
{
    /// <summary>
    /// Create a GrampsApiClient configured from environment variables.
    /// </summary>
    public static GrampsApiClient CreateClient()
    {
        var config = GrampsConfig.FromEnvironment();
        var httpClient = new HttpClient();
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<GrampsApiClient>();
        var tokenProvider = new GrampsAuthTokenProvider(
            new HttpClient(),
            config,
            loggerFactory.CreateLogger<GrampsAuthTokenProvider>());

        return new GrampsApiClient(httpClient, config, logger, tokenProvider);
    }
}
