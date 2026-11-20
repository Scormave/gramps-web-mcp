using System.Net;
using System.Text.Json;
using GrampsWeb.Mcp.Exceptions;

namespace GrampsWeb.Mcp.Client;

/// <summary>
/// Static utility to detect Gramps ID patterns and resolve them to opaque API handles.
/// Gramps IDs follow patterns like I0001 (person), F0023 (family), E0005 (event), etc. by default, and trees
/// may use other formats, such as S_ABC. Handles are opaque strings (hex-like, typically 20+ chars).
/// </summary>
public static class HandleResolver
{
    /// <summary>
    /// Returns <c>true</c> if the value looks like a Gramps ID in the default format
    /// (exactly 1 uppercase letter followed by 1 to 14 digits, e.g. I0001, F23, I00000001).
    /// Returns <c>false</c> for handles (longer hex strings), empty strings, IDs in other formats, etc.
    /// </summary>
    public static bool LooksLikeGrampsId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2 || value.Length > 15)
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
    /// Returns <c>true</c> if the value looks like a handle rather than a name: 16 or more ASCII
    /// letters, digits, <c>'-'</c> or <c>'_'</c>, at least one of them a digit
    /// (e.g. <c>102a49c11b31375183b9ad45a991</c>).
    /// </summary>
    public static bool LooksLikeHandle(string value) =>
        value.Length >= 16
        && value.Any(char.IsAsciiDigit)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// Returns <c>true</c> if the value may be a Gramps ID of a record of a known type: an ID in the default
    /// format, or any value that does not look like a handle, such as an ID in a custom format (S_ABC).
    /// </summary>
    public static bool MayBeGrampsId(string value) =>
        !string.IsNullOrWhiteSpace(value) && (LooksLikeGrampsId(value) || !LooksLikeHandle(value));

    /// <summary>
    /// Maps a single-letter Gramps ID prefix to the API object type used in list endpoints.
    /// Prefixes follow the Gramps defaults; media objects use <c>'O'</c>, not <c>'M'</c>, and tags have no Gramps ID.
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
        'O' => "media",
        _ => null,
    };

    /// <summary>
    /// Tries to resolve a possible Gramps ID to an opaque handle by querying the API.
    /// With <paramref name="expectedObjectType"/> (an API collection such as <c>sources</c>), any value that
    /// <see cref="MayBeGrampsId"/> is looked up among the records of that type, whatever its format or prefix, so
    /// IDs in custom formats (S_ABC) resolve too. Without it, only an ID in the default format is looked up, in the
    /// type its prefix names. A value that looks like a handle is returned as-is.
    /// If no object has that Gramps ID, or the API rejects the lookup as a bad request (400, 404, 422),
    /// returns the original value so the caller gets a meaningful 404 from its own request.
    /// Other failures (server errors, network errors, timeouts) propagate, so they are not
    /// misreported as a missing object.
    /// </summary>
    public static async Task<string> ResolveToHandleAsync(
        string handleOrGrampsId,
        GrampsApiClient client,
        string? expectedObjectType = null)
    {
        string? objectType;
        if (expectedObjectType is not null)
        {
            // Tags have no Gramps ID.
            if (!MayBeGrampsId(handleOrGrampsId)
                || string.Equals(expectedObjectType, "tags", StringComparison.OrdinalIgnoreCase))
                return handleOrGrampsId;
            objectType = expectedObjectType.ToLowerInvariant();
        }
        else
        {
            if (!LooksLikeGrampsId(handleOrGrampsId))
                return handleOrGrampsId;
            objectType = PrefixToObjectType(handleOrGrampsId[0]);
            if (objectType is null or "tags")
                return handleOrGrampsId;
        }

        return await FindHandleAsync(handleOrGrampsId, objectType, client) ?? handleOrGrampsId;
    }

    /// <summary>
    /// The handle of the record of <paramref name="objectType"/> (an API collection such as <c>people</c>) that has
    /// this Gramps ID, whatever its prefix, or <c>null</c> when there is none or the API rejects the lookup as a bad
    /// request (400, 404, 422). Tags have no Gramps ID, so a tag lookup is <c>null</c> without a request. Other
    /// failures (server errors, network errors, timeouts) propagate. Found handles are cached per tree and type.
    /// </summary>
    public static async Task<string?> FindHandleAsync(string grampsId, string objectType, GrampsApiClient client)
    {
        if (string.Equals(objectType, "tags", StringComparison.OrdinalIgnoreCase))
            return null;

        var scopeKey = client.CacheScopeKey;
        // The same ID can name records of two types in a tree with custom ID formats.
        var cacheId = $"{objectType}/{grampsId}";

        var cached = HandleCache.TryGet(scopeKey, cacheId);
        if (cached is not null)
            return cached;

        await HandleCache.AcquireKeyLockAsync(scopeKey, cacheId);
        try
        {
            // Double-check after acquiring lock.
            cached = HandleCache.TryGet(scopeKey, cacheId);
            if (cached is not null)
                return cached;

            try
            {
                var path = $"/api/{objectType}/?gramps_id={Uri.EscapeDataString(grampsId)}&pagesize=1";
                var raw = await client.GetAsync<JsonElement>(path);

                // The response is either a JSON array or an object with an "objects" array.
                var items = raw.ValueKind == JsonValueKind.Array
                    ? raw
                    : raw.TryGetProperty("objects", out var objArr) ? objArr : raw;

                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
                {
                    var first = items[0];
                    // Only the record with this exact ID, not the first record of a list the filter did not narrow.
                    var otherId = first.TryGetProperty("gramps_id", out var idProp)
                                  && idProp.ValueKind == JsonValueKind.String
                                  && !string.Equals(idProp.GetString(), grampsId, StringComparison.Ordinal);
                    if (!otherId &&
                        first.TryGetProperty("handle", out var handleProp) &&
                        handleProp.ValueKind == JsonValueKind.String)
                    {
                        var handle = handleProp.GetString();
                        if (!string.IsNullOrEmpty(handle))
                        {
                            HandleCache.Set(scopeKey, cacheId, handle);
                            return handle;
                        }
                    }
                }
            }
            catch (GrampsApiException ex) when (ex.StatusCode is
                HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            {
                // The lookup itself was rejected: the caller reports the ID as not found.
            }

            return null;
        }
        finally
        {
            HandleCache.ReleaseKeyLock(scopeKey, cacheId);
        }
    }
}
