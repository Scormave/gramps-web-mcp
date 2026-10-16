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
        "Read the server's reference for writing valid arguments; it holds no family records. Call it before a create_*, " +
        "update_* or list_objects call when a date, name, link update, type or filter rule is unclear; to read the tree itself " +
        "use search, list_objects or get_object. topic picks the text: input-guide (argument formats), types (valid types, " +
        "with the tree's custom ones), metadata (server and tree info), name-settings (name formats, surname groups) or " +
        "filter-rules (rules for list_objects, saved filters). section returns one part of a topic, e.g. topic types with " +
        "section event_types; an unknown topic or section is rejected with the valid values. Returns input-guide as fixed " +
        "JSON and the other topics as plain text read live from the tree. Clients that read MCP resources get the same " +
        "texts at gramps://input-guide and its siblings.")]
    public static async Task<string> GetReference(
        [Description("Which reference: input-guide, types, metadata, name-settings or filter-rules.")]
        string topic,
        [Description("One part only (optional; omit for the whole topic). input-guide: dates, name_schema, link_updates, structured_fields, or one of structured_fields.names, .attributes, .urls, .addresses, .person_associations, .repository_refs. types: a category such as event_types or place_types. name-settings: formats or groups. filter-rules: a type such as people or events (its rules and values; omit for an overview). metadata has no sections.")]
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
                "filter-rules" => await GrampsResources.FetchFilterRulesTextAsync(client, section),
                _ => throw McpToolErrors.ValidationError(
                    "Invalid topic. Must be one of: input-guide, types, metadata, name-settings, filter-rules.")
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
