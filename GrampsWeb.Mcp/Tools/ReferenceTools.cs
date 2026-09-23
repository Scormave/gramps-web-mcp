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
        "Read-only: compatibility access to one MCP reference resource. Choose input-guide, types, metadata, or name-settings. " +
        "Use section to return one input-guide subsection, one type category such as event_types, or formats/groups from name-settings.")]
    public static async Task<string> GetReference(
        [Description("Reference topic: input-guide | types | metadata | name-settings.")]
        string topic,
        [Description("Optional section. input-guide: dates | name_schema | structured_fields | structured_fields.names | structured_fields.attributes | structured_fields.urls | structured_fields.addresses | structured_fields.person_associations | structured_fields.repository_refs. types: a category key such as event_types. name-settings: formats | groups. Not supported for metadata.")]
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
