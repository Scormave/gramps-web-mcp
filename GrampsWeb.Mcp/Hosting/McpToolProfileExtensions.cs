using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Client;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
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
    /// tool still return its normal safety or configuration error. Call arguments are checked
    /// against each tool's input schema by <see cref="ToolArgumentValidator"/>.
    /// </summary>
    public static IMcpServerBuilder WithGrampsToolProfile(
        this IMcpServerBuilder builder,
        GrampsConfig config)
    {
        builder.WithToolsFromAssembly();
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            async (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is not McpServerTool tool)
                    return await next(request, cancellationToken);
                var schema = tool.ProtocolTool.InputSchema;
                var arguments = request.Params?.Arguments;
                if (ToolArgumentValidator.CheckNames(schema, arguments) is { } nameError)
                    throw new McpException(nameError);
                if (ToolArgumentValidator.NumbersAsText(schema, arguments) is { } converted)
                    request.Params!.Arguments = arguments = converted;
                try
                {
                    return await next(request, cancellationToken);
                }
                // The SDK hides the message of a value it cannot convert; name the argument instead.
                catch (JsonException) when (ToolArgumentValidator.FindTypeMismatch(schema, arguments) is { } typeError)
                {
                    throw new McpException(typeError);
                }
            }));
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            async (request, cancellationToken) =>
            {
                using var scope = request.MatchedPrimitive is McpServerTool tool
                    && tool.ProtocolTool.Annotations?.ReadOnlyHint == true
                        ? GrampsReadScope.Begin() : null;
                return await next(request, cancellationToken);
            }));

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
        tool.Name == "read_media";

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
