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
        "Find records by words in them: one query across people, families, events, places, sources, citations, repositories, " +
        "notes, media and tags. Start here when you have a name, place, title or other text but no Gramps ID. " +
        "Returns one line per hit (type, Gramps ID, summary, handle) with the page and total count; " +
        "pass a Gramps ID or handle to get_object for the full record. " +
        "To browse or filter one type by its fields (all people, citations of one source, sorted by change) use list_objects. " +
        "Very long queries can fail on the server; use a shorter term then.")]
    public static async Task<string> Search(
        [Description("Words to find; * matches any ending. Examples: Smith*, John, Dublin*, a register title.")]
        string query,
        [Description("Page number, from 1 (default 1).")]
        int page = 1,
        [Description("Hits per page, 1–100 (default 20).")]
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
        "List the records of one type, a page at a time, optionally filtered and sorted: one line per record " +
        "(Gramps ID, summary, handle) with the page and total count. Use it to browse a type, look up one Gramps ID, " +
        "list the citations of a source, or filter by fields with Gramps Query Language; to find records by text use search, " +
        "and to read one record in full use get_object.")]
    public static async Task<string> ListObjects(
        [Description("Which records to list: people, families, events, places, sources, citations, repositories, notes, media or tags.")]
        string objectType,
        [Description("Page number, from 1 (default 1).")]
        int page = 1,
        [Description("Records per page, 1–100 (default 20).")]
        int pagesize = 20,
        [Description("Only the record with this Gramps ID, e.g. I0001 (not a handle; optional).")]
        string? grampsId = null,
        [Description("With objectType citations only: list just the citations of this source; combined with gql by and (optional). " + ToolDescriptionFragments.HandleDiscovery)]
        string? sourceHandle = null,
        [Description("Filter in Gramps Query Language, run on the server, e.g. gender == 1 or media_list.length >= 1 (optional).")]
        string? gql = null,
        [Description("Field to sort by, e.g. gramps_id; a leading - sorts descending, e.g. -change for latest edits first (optional).")]
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
