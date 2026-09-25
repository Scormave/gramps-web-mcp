using GrampsWeb.Mcp.Config;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Hosting;

/// <summary>
/// Configures the MCP tool catalog for the server's enabled capabilities.
/// </summary>
internal static class McpToolProfileExtensions
{
    /// <summary>
    /// Registers every tool while publishing only tools enabled by the current configuration.
    /// Tools remain registered for compatibility, so direct calls to a hidden write or media-byte
    /// tool still return its normal safety or configuration error.
    /// </summary>
    public static IMcpServerBuilder WithGrampsToolProfile(
        this IMcpServerBuilder builder,
        GrampsConfig config)
    {
        builder.WithToolsFromAssembly();

        if (config.ReadOnly || !config.MediaResourcesEnabled)
        {
            builder.WithRequestFilters(filters => filters.AddListToolsFilter(next =>
                (request, cancellationToken) => FilterAvailableToolsAsync(next, request, cancellationToken, config)));
        }

        return builder;
    }

    internal static ListToolsResult KeepAvailableTools(ListToolsResult result, GrampsConfig config)
    {
        result.Tools = result.Tools
            .Where(tool => (!config.ReadOnly || tool.Annotations?.ReadOnlyHint == true)
                           && (config.MediaResourcesEnabled || !IsMediaByteTool(tool)))
            .ToList();
        return result;
    }

    private static bool IsMediaByteTool(Tool tool) =>
        tool.Name is "get_media_thumbnail" or "get_media_file";

    private static async ValueTask<ListToolsResult> FilterAvailableToolsAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken,
        GrampsConfig config)
    {
        var result = await next(request, cancellationToken);
        return KeepAvailableTools(result, config);
    }
}
