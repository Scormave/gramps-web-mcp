using System.ComponentModel;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP Server tools for searching and listing Gramps objects with pagination support.
/// </summary>
[McpServerToolType]
public static class SearchTools
{
    [McpServerTool]
    [Description(
        "Full-text search across all Gramps object types (people, families, events, places, sources, " +
        "citations, repositories, notes, media, tags). " +
        "Supports wildcards (*). Example: search('Smith*') finds all people, events, places with 'Smith' in any field. " +
        "Returns handles and object_types — use those handles with get_person, get_family, get_event, etc. " +
        "Supports pagination with page and pagesize parameters.")]
    public static async Task<string> Search(
        [Description("Search query. Supports wildcards (*). Example: 'Smith*', 'John', 'Dublin*'")]
        string query,
        [Description("Page number (1-indexed). Default: 1")]
        int page = 1,
        [Description("Results per page. Default: 20, max: 100")]
        int pagesize = 20,
        GrampsApiClient client = null!)
    {
        try
        {
            if (page < 1) page = 1;
            if (pagesize < 1) pagesize = 20;
            if (pagesize > 100) pagesize = 100;

            var queryString = $"/api/search/?query={Uri.EscapeDataString(query)}&page={page}&pagesize={pagesize}";

            var raw = await client.GetAsync<JsonElement>(queryString);
            var hits = ParseSearchHits(raw);

            if (hits.Length == 0)
                return $"No results found for '{query}'";

            return await SearchFormatter.FormatSearchResults(hits, client);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    [McpServerTool]
    [Description(
        "List objects of a specific type (people, families, events, places, sources, citations, " +
        "repositories, notes, media, tags) with pagination and optional filtering. " +
        "This is the single entry for browsing paginated lists. " +
        "For citations only: pass sourceHandle to restrict to one source. " +
        "Tags are paged like other types (pagesize max 100); use multiple pages if you have many tags. " +
        "Parameters: " +
        "  objectType: people | families | events | places | sources | citations | repositories | notes | media | tags " +
        "  page: Page number (1-indexed), default 1 " +
        "  pagesize: Results per page, default 20, max 100 " +
        "  grampsId: Filter by specific Gramps ID " +
        "  sourceHandle: When objectType is citations, optional filter by source handle " +
        "  gql: Gramps Query Language filter (e.g. 'media_list.length >= 1' or 'gender == 1') " +
        "  sort: Sort field, prefix with '-' for descending (e.g. 'gramps_id' or '-change')")]
    public static async Task<string> ListObjects(
        [Description("Type of objects: people | families | events | places | sources | citations | repositories | notes | media | tags")]
        string objectType,
        [Description("Page number (1-indexed). Default: 1")]
        int page = 1,
        [Description("Results per page. Default: 20, max: 100")]
        int pagesize = 20,
        [Description("Optional: Filter by Gramps ID")]
        string? grampsId = null,
        [Description("Optional: When objectType is citations, filter by source handle")]
        string? sourceHandle = null,
        [Description("Optional: Gramps QL filter query (e.g. 'media_list.length >= 1')")]
        string? gql = null,
        [Description("Optional: Sort field. Prefix with '-' for descending (e.g. 'gramps_id' or '-change')")]
        string? sort = null,
        GrampsApiClient client = null!)
    {
        try
        {
            var validTypes = new[]
            {
                "people", "families", "events", "places", "sources",
                "citations", "repositories", "notes", "media", "tags"
            };

            if (!validTypes.Contains(objectType.ToLower()))
                throw McpToolErrors.ValidationError(
                    $"Invalid object_type. Must be one of: {string.Join(", ", validTypes)}");

            if (page < 1) page = 1;
            if (pagesize < 1) pagesize = 20;
            if (pagesize > 100) pagesize = 100;

            var queryParams = new List<string>
            {
                $"page={page}",
                $"pagesize={pagesize}"
            };

            if (!string.IsNullOrEmpty(grampsId))
                queryParams.Add($"gramps_id={Uri.EscapeDataString(grampsId)}");

            if (!string.IsNullOrEmpty(gql))
                queryParams.Add($"gql={Uri.EscapeDataString(gql)}");

            if (!string.IsNullOrEmpty(sort))
                queryParams.Add($"sort={Uri.EscapeDataString(sort)}");

            // Match search formatting: embed father/mother in one response (apispec: query name is extend).
            if (objectType.Equals("families", StringComparison.OrdinalIgnoreCase))
                queryParams.Add("extend=father_handle,mother_handle");

            if (objectType.Equals("events", StringComparison.OrdinalIgnoreCase))
                queryParams.Add("extend=place");

            if (objectType.Equals("citations", StringComparison.OrdinalIgnoreCase))
            {
                queryParams.Add("extend=source_handle");
                if (!string.IsNullOrEmpty(sourceHandle))
                    queryParams.Add($"source_handle={Uri.EscapeDataString(sourceHandle)}");
            }

            var queryString = $"/api/{objectType.ToLower()}/?{string.Join("&", queryParams)}";

            return objectType.ToLower() switch
            {
                "people" => await SearchFormatter.FetchAndFormatObjects<GrampsPerson>(queryString, client, objectType),
                "families" => await SearchFormatter.FetchAndFormatObjects<GrampsFamilyExtended>(queryString, client, objectType),
                "events" => await SearchFormatter.FetchAndFormatObjects<GrampsEventExtended>(queryString, client, objectType),
                "places" => await SearchFormatter.FetchAndFormatObjects<GrampsPlace>(queryString, client, objectType),
                "sources" => await SearchFormatter.FetchAndFormatObjects<GrampsSource>(queryString, client, objectType),
                "citations" => await SearchFormatter.FetchAndFormatObjects<GrampsCitationExtended>(queryString, client, objectType),
                "repositories" => await SearchFormatter.FetchAndFormatObjects<GrampsRepository>(queryString, client, objectType),
                "notes" => await SearchFormatter.FetchAndFormatObjects<GrampsNote>(queryString, client, objectType),
                "media" => await SearchFormatter.FetchAndFormatObjects<GrampsMedia>(queryString, client, objectType),
                "tags" => await SearchFormatter.FetchAndFormatObjects<GrampsTag>(queryString, client, objectType),
                _ => throw new McpException("Invalid object type")
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    private static GrampsSearchHit[] ParseSearchHits(JsonElement raw)
    {
        // Current Gramps Web search shape: array of hits.
        if (raw.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<GrampsSearchHit[]>(raw.GetRawText(), GrampsJson.Options)
                ?? Array.Empty<GrampsSearchHit>();
        }

        // Backward/alternate shape: paged object with "objects".
        if (raw.ValueKind == JsonValueKind.Object &&
            raw.TryGetProperty("objects", out var objectsElement) &&
            objectsElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<GrampsSearchHit[]>(objectsElement.GetRawText(), GrampsJson.Options)
                ?? Array.Empty<GrampsSearchHit>();
        }

        return Array.Empty<GrampsSearchHit>();
    }
}
