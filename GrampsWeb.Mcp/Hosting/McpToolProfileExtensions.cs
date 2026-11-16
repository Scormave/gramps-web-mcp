using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Input;
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
    /// Tools remain registered for compatibility, so direct calls to a hidden write, media-byte or
    /// upload tool still return its normal safety or configuration error. Call arguments are checked
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

        builder.WithRequestFilters(filters => filters.AddListToolsFilter(next =>
            (request, cancellationToken) => FilterAvailableToolsAsync(next, request, cancellationToken, config)));

        return builder;
    }

    internal static ListToolsResult KeepAvailableTools(ListToolsResult result, GrampsConfig config)
    {
        result.Tools = result.Tools
            .Where(tool => (!config.ReadOnly || tool.Annotations?.ReadOnlyHint == true)
                           && (config.MediaResourcesEnabled || !IsMediaByteTool(tool))
                           && (config.MediaUploadEnabled || !IsMediaUploadTool(tool)))
            .Select(tool => WithAvailableUploadSources(tool, config))
            .ToList();
        return result;
    }

    /// <summary>
    /// create_media and update_media with only the upload sources the configuration allows: file and its
    /// <c>openai/fileParams</c> entry only when attached files may be downloaded, and filePath unless attached
    /// files are the only source. create_media then requires the one left. A listed file is typed as the file
    /// object clients fill in (<c>"object"</c> whose four fields are all <c>"string"</c>), not as nullable. Other
    /// tools are returned as they are. The registered tool is not changed: calls are still checked against it.
    /// </summary>
    internal static Tool WithAvailableUploadSources(Tool tool, GrampsConfig config)
    {
        if (tool.Name is not ("create_media" or "update_media"))
            return tool;
        var hide = !config.MediaUploadFromLinksEnabled ? FileReference.ArgumentName
            : !config.MediaUploadFromFoldersEnabled ? "filePath"
            : null;
        var keep = hide == "filePath" ? FileReference.ArgumentName : "filePath";

        var node = JsonSerializer.SerializeToNode(tool, McpJsonUtilities.DefaultOptions)!.AsObject();
        var schema = node["inputSchema"]!.AsObject();
        if (schema["properties"]?[FileReference.ArgumentName] is JsonObject file)
        {
            file["type"] = "object";
            file.Remove("default");
            foreach (var name in new[] { "download_url", "file_id", "mime_type", "file_name" })
            {
                if (file["properties"]?[name] is JsonObject field)
                    field["type"] = "string";
            }
        }

        if (hide is null)
            return node.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!;
        schema["properties"]?.AsObject().Remove(hide);
        var required = (schema["required"]?.AsArray().Select(name => name!.GetValue<string>()) ?? [])
            .Where(name => name != hide)
            .ToList();
        if (tool.Name == "create_media" && !required.Contains(keep))
            required.Insert(0, keep);
        if (required.Count > 0)
            schema["required"] = new JsonArray(required.Select(name => (JsonNode?)name).ToArray());
        else
            schema.Remove("required");

        if (hide == FileReference.ArgumentName && node["_meta"] is JsonObject meta)
        {
            meta.Remove(FileReference.FileParamsMeta);
            if (meta.Count == 0)
                node.Remove("_meta");
        }

        return node.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!;
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
