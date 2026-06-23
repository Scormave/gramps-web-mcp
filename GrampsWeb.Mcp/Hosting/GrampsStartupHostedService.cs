using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Verifies Gramps Web connectivity on startup and logs a clear connection summary.
/// </summary>
public sealed class GrampsStartupHostedService : IHostedService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly GrampsHealthService _healthService;
    private readonly GrampsConfig _config;
    private readonly McpTransportConfig? _transport;
    private readonly ILogger<GrampsStartupHostedService> _logger;

    public GrampsStartupHostedService(
        GrampsHealthService healthService,
        GrampsConfig config,
        ILogger<GrampsStartupHostedService> logger,
        IServiceProvider services)
    {
        _healthService = healthService;
        _config = config;
        _transport = services.GetService<McpTransportConfig>();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogTransportSummary();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var status = await _healthService.CheckAsync(cancellationToken);
            if (status.IsHealthy)
            {
                LogConnected(status);
                return;
            }

            if (attempt == MaxAttempts)
            {
                _logger.LogError(
                    "Could not connect to Gramps Web at {ApiUrl} after {Attempts} attempts: {Error}. " +
                    "The MCP server will keep running; fix GRAMPS_API_URL, credentials, or GRAMPS_TREE_ID " +
                    "and check /health when using HTTP transport.",
                    status.ApiUrl,
                    MaxAttempts,
                    status.Error);
                return;
            }

            _logger.LogWarning(
                "Waiting for Gramps Web at {ApiUrl} (attempt {Attempt}/{MaxAttempts}): {Error}",
                status.ApiUrl,
                attempt,
                MaxAttempts,
                status.Error);

            await Task.Delay(RetryDelay, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void LogTransportSummary()
    {
        if (_transport is null)
        {
            _logger.LogInformation("MCP transport: stdio");
            return;
        }

        _logger.LogInformation(
            "MCP transport: {Mode} at {Path}{Legacy}",
            _transport.Mode,
            _transport.MapPath,
            _transport.EnableLegacySse ? " (legacy SSE enabled)" : string.Empty);
    }

    private void LogConnected(GrampsConnectivityStatus status)
    {
        var treeLabel = status.TreeName ?? status.ConfiguredTreeId;
        if (status.TreeDatabaseId is not null && status.TreeDatabaseId != status.ConfiguredTreeId)
        {
            treeLabel = $"{treeLabel} (database id {status.TreeDatabaseId})";
        }

        _logger.LogInformation(
            "Connected to Gramps Web at {ApiUrl} (tree: {Tree}, configured GRAMPS_TREE_ID={TreeId})",
            status.ApiUrl,
            treeLabel,
            status.ConfiguredTreeId);

        if (status.GrampsVersion is not null)
        {
            _logger.LogInformation("Gramps version: {Version}", status.GrampsVersion);
        }

        if (_config.ReadOnly)
        {
            _logger.LogInformation("Read-only mode is enabled; mutation tools will be rejected");
        }
    }
}
