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
    [McpServerTool(Title = "Search", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: full-text search across all object types (people, families, events, places, sources, citations, repositories, notes, media, tags). " +
        "Use * wildcards (e.g. Smith*). " +
        "Results include handles—pass them to get_person, get_event, list_objects for more rows, etc. " +
        "Paginate with page and pagesize.")]
    public static async Task<string> Search(
        [Description("Query string; * is wildcard. Examples: Smith*, John, Dublin*")]
        string query,
        [Description("1-based page index. Default 1.")]
        int page = 1,
        [Description("Page size. Default 20, maximum 100.")]
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

    [McpServerTool(Title = "List Objects", ReadOnly = true, Destructive = false)]
    [Description(
        "Read-only: paginated list of one object type. Primary way to browse the tree when you know the type. " +
        "objectType must be exactly: people, families, events, places, sources, citations, repositories, notes, media, or tags (lowercase). " +
        "For citations only, optional sourceHandle limits rows to one source. " +
        "Advanced: gql is Gramps Query Language (e.g. media_list.length >= 1, gender == 1). sort is a field name; prefix with - for descending (gramps_id, -change). " +
        "Maximum pagesize 100—advance page for more.")]
    public static async Task<string> ListObjects(
        [Description("Object collection: people | families | events | places | sources | citations | repositories | notes | media | tags (exact spelling, case-insensitive).")]
        string objectType,
        [Description("1-based page. Default 1.")]
        int page = 1,
        [Description("Page size. Default 20, max 100.")]
        int pagesize = 20,
        [Description("Optional. Filter by numeric/string Gramps ID (I0001-style), NOT the opaque handle.")]
        string? grampsId = null,
        [Description("Optional. When objectType is citations, limit to citations of this source handle. " + ToolDescriptionFragments.HandleDiscovery)]
        string? sourceHandle = null,
        [Description("Optional. Gramps QL expression executed server-side for filtering.")]
        string? gql = null,
        [Description("Optional. Sort field; leading - means descending.")]
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
                {
                    var resolvedSourceHandle = await HandleResolver.ResolveToHandleAsync(sourceHandle, client, "sources");
                    queryParams.Add($"source_handle={Uri.EscapeDataString(resolvedSourceHandle)}");
                }
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
