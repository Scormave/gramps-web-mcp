using System.Text.Json;
using GrampsWeb.Mcp.Serialization;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Static utility to detect Gramps ID patterns and resolve them to opaque API handles.
/// Gramps IDs follow patterns like I0001 (person), F0023 (family), E0005 (event), etc.
/// Handles are opaque strings (hex-like, typically 20+ chars).
/// </summary>
public static class HandleResolver
{
    /// <summary>
    /// Returns <c>true</c> if the value looks like a Gramps ID
    /// (exactly 1 uppercase letter followed by 1+ digits, e.g. I0001, F23, P0005).
    /// Returns <c>false</c> for handles (longer hex strings), empty strings, etc.
    /// </summary>
    public static bool LooksLikeGrampsId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2 || value.Length > 8)
            return false;

        if (value[0] < 'A' || value[0] > 'Z')
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Maps a single-letter Gramps ID prefix to the API object type used in list endpoints.
    /// </summary>
    /// <returns>
    /// The API path segment (e.g. <c>"people"</c> for <c>'I'</c>), or <c>null</c> if the
    /// prefix does not match a known Gramps object type.
    /// </returns>
    public static string? PrefixToObjectType(char prefix) => prefix switch
    {
        'I' => "people",
        'F' => "families",
        'E' => "events",
        'P' => "places",
        'S' => "sources",
        'C' => "citations",
        'R' => "repositories",
        'N' => "notes",
        'M' => "media",
        'T' => "tags",
        _ => null,
    };

    /// <summary>
    /// Tries to resolve a possible Gramps ID to an opaque handle by querying the API.
    /// If the value doesn't look like a Gramps ID, returns it as-is (it's probably already a handle).
    /// If resolution fails (not found or API error), returns the original value so the caller
    /// gets a meaningful 404 rather than a cryptic error.
    /// </summary>
    public static async Task<string> ResolveToHandleAsync(string handleOrGrampsId, GrampsApiClient client)
    {
        if (!LooksLikeGrampsId(handleOrGrampsId))
            return handleOrGrampsId;

        var objectType = PrefixToObjectType(handleOrGrampsId[0]);
        if (objectType is null)
            return handleOrGrampsId;

        try
        {
            var path = $"/api/{objectType}/?gramps_id={Uri.EscapeDataString(handleOrGrampsId)}&pagesize=1";
            var raw = await client.GetAsync<JsonElement>(path);

            // The response is either a JSON array or an object with an "objects" array.
            var items = raw.ValueKind == JsonValueKind.Array
                ? raw
                : raw.TryGetProperty("objects", out var objArr) ? objArr : raw;

            if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                var first = items[0];
                if (first.TryGetProperty("handle", out var handleProp) &&
                    handleProp.ValueKind == JsonValueKind.String)
                {
                    var handle = handleProp.GetString();
                    if (!string.IsNullOrEmpty(handle))
                        return handle;
                }
            }
        }
        catch
        {
            // Graceful degradation: return the original value and let the caller surface the error.
        }

        return handleOrGrampsId;
    }
}
