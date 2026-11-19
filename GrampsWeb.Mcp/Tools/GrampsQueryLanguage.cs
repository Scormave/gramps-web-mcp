using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Gramps Web's two query languages for the list endpoints. Gramps QL, the <c>gql</c> query argument, was removed
/// in API 3.26; GOQL, a Python-like expression the <c>MatchesQuery</c> filter rule runs, came in API 3.23. Lookups
/// this server makes on its own use GOQL where the server has it and Gramps QL on older servers.
/// </summary>
internal static class GrampsQueryLanguage
{
    /// <summary>The first Gramps Web API version with the MatchesQuery filter rule.</summary>
    internal static readonly Version MatchesQuerySince = new(3, 23);

    /// <summary>What list_records says when the server refuses its gql argument.</summary>
    internal const string GqlRemoved =
        "This Gramps Web server (API 3.26 or later) no longer has Gramps QL, so gql cannot be used. " +
        "Filter by field values with the MatchesQuery rule in rules instead. Its GOQL expression is Python-like: " +
        "== for equals, and, or, not, text in single quotes, len(list) for a list's length; for example " +
        "rules: {\"rules\": [{\"name\": \"MatchesQuery\", \"values\": [\"primary_name.first_name == 'Anna'\"]}]}.";

    // The JSON goes into a query string, so the quotes of a GOQL expression can stay unescaped.
    private static readonly JsonSerializerOptions RulesJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Whether the server has the MatchesQuery rule: API 3.23 or later by <c>/api/metadata/</c>. A server whose
    /// version cannot be read counts as having it, since it may be one without Gramps QL.
    /// </summary>
    public static async Task<bool> HasMatchesQueryAsync(GrampsApiClient client)
    {
        JsonElement metadata;
        try
        {
            metadata = await client.GetAsync<JsonElement>("/api/metadata/");
        }
        catch (GrampsApiException)
        {
            return true;
        }

        return !(GrampsServerVersion.ReadApiVersion(metadata) is { } version
                 && GrampsServerVersion.Parse(version) is { } parsed
                 && parsed < MatchesQuerySince);
    }

    /// <summary>The rules argument of a list endpoint: one MatchesQuery rule with <paramref name="expression"/>.</summary>
    public static string MatchesQueryRules(string expression) =>
        JsonSerializer.Serialize(new { rules = new[] { new { name = "MatchesQuery", values = new[] { expression } } } }, RulesJson);

    /// <summary>
    /// Whether a list request failed because the server does not take the gql argument: API 3.26 answers it with
    /// HTTP 422 and <c>{"error": {"messages": {"query": {"gql": ["Unknown field."]}}}}</c>.
    /// </summary>
    public static bool IsGqlRefused(GrampsApiException error)
    {
        if (error.StatusCode != HttpStatusCode.UnprocessableEntity)
            return false;
        try
        {
            using var document = JsonDocument.Parse(error.ResponseBody);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var body) && body.ValueKind == JsonValueKind.Object
                   && body.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Object
                   && messages.TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.Object
                   && query.TryGetProperty("gql", out var gql) && gql.ValueKind == JsonValueKind.Array
                   && gql.EnumerateArray().Any(message => message.ValueKind == JsonValueKind.String
                       && message.GetString()!.StartsWith("Unknown field", StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
