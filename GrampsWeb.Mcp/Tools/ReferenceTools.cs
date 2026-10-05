using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Resources;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP compatibility tool for resource-like discovery data. Use when clients do not support resources/read.
/// </summary>
[McpServerToolType]
public static class ReferenceTools
{
    [McpServerTool(Title = "Get Reference", ReadOnly = true, Destructive = false)]
    [Description(
        "Read the server's reference text before writing when a format or type is unclear. Topics: input-guide (how to " +
        "write dates, names, link updates and structured fields such as attributes and addresses), types (the valid event, " +
        "role, place, note and other types, including custom ones the tree uses), metadata (server and tree info), and " +
        "name-settings (name display formats and surname groups). section narrows the reply to one part; an unknown topic " +
        "or section is rejected with the valid ones. The same texts are MCP resources (gramps://input-guide and so on) for clients that read resources.")]
    public static async Task<string> GetReference(
        [Description("Which reference: input-guide, types, metadata or name-settings.")]
        string topic,
        [Description("One part only (optional; omit for the whole topic). input-guide: dates, name_schema, link_updates, structured_fields, or one of structured_fields.names, .attributes, .urls, .addresses, .person_associations, .repository_refs. types: a category such as event_types or place_types. name-settings: formats or groups. metadata has no sections.")]
        string? section = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var normalizedTopic = topic.Trim().ToLowerInvariant();
            return normalizedTopic switch
            {
                "input-guide" => GrampsResources.BuildInputGuideText(section),
                "types" => await GrampsResources.FetchTypesTextAsync(client, section),
                "metadata" => await GetUnsectionedReferenceAsync(
                    normalizedTopic, section, () => GrampsResources.FetchMetadataTextAsync(client)),
                "name-settings" => await GrampsResources.FetchNameSettingsTextAsync(client, section),
                _ => throw McpToolErrors.ValidationError(
                    "Invalid topic. Must be one of: input-guide, types, metadata, name-settings.")
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static async Task<string> GetUnsectionedReferenceAsync(
        string topic,
        string? section,
        Func<Task<string>> fetch)
    {
        if (!string.IsNullOrWhiteSpace(section))
            throw McpToolErrors.ValidationError($"section is not supported for topic {topic}.");

        return await fetch();
    }
}
