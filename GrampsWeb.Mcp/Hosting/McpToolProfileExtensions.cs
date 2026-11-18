using System.Text.Json;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
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
    /// Tool metadata that makes clients which honour it, such as Claude Code, ask the user on every call.
    /// delete_object, merge_objects and undo_change carry it by attribute.
    /// </summary>
    internal const string RequiresUserInteractionMeta = "anthropic/requiresUserInteraction";

    /// <summary>
    /// Registers every tool while publishing only tools enabled by the current configuration.
    /// Tools remain registered for compatibility, so direct calls to a hidden write, media-byte or
    /// upload tool still return its normal safety or configuration error. Call arguments are checked
    /// against each tool's input schema by <see cref="ToolArgumentValidator"/>.
    /// With <c>GRAMPS_REQUIRE_USER_INTERACTION=false</c> the tools are published without
    /// <see cref="RequiresUserInteractionMeta"/>.
    /// </summary>
    public static IMcpServerBuilder WithGrampsToolProfile(
        this IMcpServerBuilder builder,
        GrampsConfig config)
    {
        builder.WithToolsFromAssembly();
        if (!config.RequireUserInteraction)
        {
            // Runs once, when the server options are built, so no tools/list reply carries the metadata.
            builder.Services.PostConfigure<McpServerOptions>(options =>
            {
                if (options.ToolCollection is not { } tools)
                    return;
                foreach (var tool in tools)
                {
                    if (tool.ProtocolTool.Meta is { } meta && meta.Remove(RequiresUserInteractionMeta) && meta.Count == 0)
                        tool.ProtocolTool.Meta = null;
                }
            });
        }
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            async (request, cancellationToken) =>
            {
                if (request.MatchedPrimitive is not McpServerTool tool)
                    return await next(request, cancellationToken);
                var schema = tool.ProtocolTool.InputSchema;
                var arguments = request.Params?.Arguments;
                if (ToolArgumentValidator.CheckNames(schema, arguments) is { } nameError)
                    throw new McpException(nameError);
                if (ToolArgumentValidator.CheckSomethingToUpdate(tool.ProtocolTool.Name, schema, arguments) is { } updateError)
                    throw new McpException(updateError);
                if (ToolArgumentValidator.NumbersAsText(schema, arguments) is { } converted)
                    request.Params!.Arguments = arguments = converted;
                try
                {
                    return await next(request, cancellationToken);
                }
                // An argument converter that knows what is wrong, such as an unknown field in a name object.
                catch (JsonInputException ex)
                {
                    throw new McpException(ex.Path is null or "$" ? ex.Message : $"{ex.Message} At {ex.Path}.");
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

        if (config.ReadOnly || !config.MediaResourcesEnabled || !config.MediaUploadEnabled)
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
                           && (config.MediaResourcesEnabled || !IsMediaByteTool(tool))
                           && (config.MediaUploadEnabled || !IsMediaUploadTool(tool)))
            .ToList();
        return result;
    }

    private static bool IsMediaByteTool(Tool tool) =>
        tool.Name == "read_media";

    private static bool IsMediaUploadTool(Tool tool) =>
        tool.Name == "create_media";

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
