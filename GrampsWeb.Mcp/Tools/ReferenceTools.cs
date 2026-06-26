using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP compatibility tools for resource-like discovery data. Use when clients do not support resources/read.
/// </summary>
[McpServerToolType]
public static class ReferenceTools
{
    [McpServerTool(Title = "Get Input Guide", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: complete write-input reference with date formats, structured fields, and full Name schema. " +
        "Same data as gramps://input-guide.")]
    public static string GetInputGuide() => GrampsResources.BuildInputGuideText();

    [McpServerTool(Title = "Get Types", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: built-in and custom type vocabularies for validating type/role/origin strings. " +
        "Same data as gramps://types.")]
    public static Task<string> GetTypes(GrampsApiClient client) =>
        GrampsResources.FetchTypesTextAsync(client);

    [McpServerTool(Title = "Get Metadata", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: connection and tree metadata (API version, tree id/name, owner, default person, etc.). " +
        "Same data as gramps://metadata.")]
    public static Task<string> GetMetadata(GrampsApiClient client) =>
        GrampsResources.FetchMetadataTextAsync(client);

    [McpServerTool(Title = "Get Name Settings", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: name display formats and surname grouping rules configured in this tree. " +
        "Same data as gramps://name-settings.")]
    public static Task<string> GetNameSettings(GrampsApiClient client) =>
        GrampsResources.FetchNameSettingsTextAsync(client);
}
