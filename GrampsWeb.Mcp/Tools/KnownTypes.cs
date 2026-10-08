using GrampsWeb.Mcp.Client;

namespace GrampsWeb.Mcp.Tools;

/// <summary>Checks the type values that the write tools send.</summary>
internal static class KnownTypes
{
    /// <summary>
    /// Returns <paramref name="value"/> in the spelling of the <paramref name="category"/> vocabulary, since Gramps
    /// would store a type sent in another case as a new custom type, and rejects a value that is neither a standard
    /// Gramps type nor a custom one the tree already uses, with suggestions.
    /// </summary>
    public static async Task<string> ResolveAsync(string value, string category, GrampsApiClient client)
    {
        var (label, error) = await TypeCache.ResolveTypeAsync(value, category, client);
        return error is null ? label! : throw McpToolErrors.ValidationError(error);
    }
}
