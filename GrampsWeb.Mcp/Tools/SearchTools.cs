using System.ComponentModel;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
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
        "Results include handles—pass them to get_object or list_objects for more rows. " +
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

            // The profile gives each hit's summary line without a read per hit.
            var queryString = $"/api/search/?query={Uri.EscapeDataString(query)}&page={page}&pagesize={pagesize}" +
                              $"&{SearchFormatter.ProfileQuery}";

            GrampsPagedResult<GrampsSearchHit> result;
            try
            {
                // Paged list read: the hit array comes with the X-Total-Count header.
                result = await client.GetPagedListAsync<GrampsSearchHit>(queryString);
            }
            catch (GrampsApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.InternalServerError)
            {
                throw new McpException(
                    "Gramps Web search returned HTTP 500 for this query. " +
                    "Try a shorter term (for example, one archive number or file number) or search the relevant object list. " +
                    "If short queries also fail, check the Gramps Web server logs and search index.", ex);
            }
            var hits = result.Objects ?? [];

            if (hits.Length == 0)
                return result.Total > 0
                    ? $"No results on page {page} for '{query}' (Total: {result.Total}); try a lower page."
                    : $"No results found for '{query}'";

            return await SearchFormatter.FormatSearchResults(hits, client, page, pagesize, result.Total);
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
        "For citations only, optional sourceHandle limits rows to one source (combined with gql using and). " +
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
                    $"Invalid objectType. Must be one of: {string.Join(", ", validTypes)}");

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

            var isCitations = objectType.Equals("citations", StringComparison.OrdinalIgnoreCase);
            if (isCitations && !string.IsNullOrEmpty(sourceHandle))
            {
                // /api/citations/ has no source_handle query parameter; filter through Gramps QL instead.
                var resolvedSourceHandle = await HandleResolver.ResolveToHandleAsync(sourceHandle, client, "sources");
                if (!resolvedSourceHandle.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    throw McpToolErrors.ValidationError(
                        "sourceHandle must be a source handle or Gramps ID (letters, digits, '-' or '_').");

                var sourceFilter = $"source_handle = \"{resolvedSourceHandle}\"";
                gql = string.IsNullOrEmpty(gql) ? sourceFilter : $"{sourceFilter} and ({gql})";
            }

            if (!string.IsNullOrEmpty(gql))
                queryParams.Add($"gql={Uri.EscapeDataString(gql)}");

            if (!string.IsNullOrEmpty(sort))
                queryParams.Add($"sort={Uri.EscapeDataString(sort)}");

            // Match search formatting: names, vitals, event places and citation sources come from the profile.
            if (objectType.ToLowerInvariant() is "people" or "families" or "events" or "citations")
                queryParams.Add(SearchFormatter.ProfileQuery);

            var queryString = $"/api/{objectType.ToLower()}/?{string.Join("&", queryParams)}";

            return objectType.ToLower() switch
            {
                "people" => await SearchFormatter.FetchAndFormatObjects<GrampsPerson>(queryString, client, objectType, pagesize),
                "families" => await SearchFormatter.FetchAndFormatObjects<GrampsFamily>(queryString, client, objectType, pagesize),
                "events" => await SearchFormatter.FetchAndFormatObjects<GrampsEvent>(queryString, client, objectType, pagesize),
                "places" => await SearchFormatter.FetchAndFormatObjects<GrampsPlace>(queryString, client, objectType, pagesize),
                "sources" => await SearchFormatter.FetchAndFormatObjects<GrampsSource>(queryString, client, objectType, pagesize),
                "citations" => await SearchFormatter.FetchAndFormatObjects<GrampsCitation>(queryString, client, objectType, pagesize),
                "repositories" => await SearchFormatter.FetchAndFormatObjects<GrampsRepository>(queryString, client, objectType, pagesize),
                "notes" => await SearchFormatter.FetchAndFormatObjects<GrampsNote>(queryString, client, objectType, pagesize),
                "media" => await SearchFormatter.FetchAndFormatObjects<GrampsMedia>(queryString, client, objectType, pagesize),
                "tags" => await SearchFormatter.FetchAndFormatObjects<GrampsTag>(queryString, client, objectType, pagesize),
                _ => throw new McpException("Invalid object type")
            };
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }
}
