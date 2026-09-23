using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Configures the MCP tool catalog for the server's access mode.
/// </summary>
internal static class McpToolProfileExtensions
{
    /// <summary>
    /// Registers every tool in read/write mode. In read-only mode, retains the complete
    /// server registration for compatibility while publishing only read-only tools to clients.
    /// Direct calls to hidden write tools remain protected by <see cref="Client.GrampsApiClient"/>.
    /// </summary>
    public static IMcpServerBuilder WithGrampsToolProfile(
        this IMcpServerBuilder builder,
        GrampsConfig config)
    {
        builder.WithToolsFromAssembly();

        if (config.ReadOnly)
        {
            builder.WithRequestFilters(filters => filters.AddListToolsFilter(next =>
                (request, cancellationToken) => FilterReadOnlyToolsAsync(next, request, cancellationToken)));
        }

        return builder;
    }

    internal static ListToolsResult KeepReadOnlyTools(ListToolsResult result)
    {
        result.Tools = result.Tools
            .Where(tool => tool.Annotations?.ReadOnlyHint == true)
            .ToList();
        return result;
    }

    private static async ValueTask<ListToolsResult> FilterReadOnlyToolsAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var result = await next(request, cancellationToken);
        return KeepReadOnlyTools(result);
    }
}
