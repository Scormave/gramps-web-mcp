namespace GrampsWeb.Mcp.Tools;

internal static class LinkUpdates
{
    public const string Description = "How to apply all supplied link lists: replace (default) replaces each full list, and a link it keeps keeps its stored privacy, citations, notes and crop unless the call sends new values; add appends missing handles and preserves existing link metadata; remove removes all links matching supplied handles (reference metadata is ignored). Omitted lists stay unchanged; empty lists clear only in replace mode. Does not affect attributes, names, addresses or URLs. Use separate calls to add and remove, or replace to edit existing link metadata.";

    public static void Validate(string mode)
    {
        if (mode is not ("replace" or "add" or "remove"))
            throw McpToolErrors.ValidationError("linkMode must be replace, add, or remove.");
    }

    public static T[]? Apply<T>(T[]? existing, T[]? supplied, string mode, Func<T, string?> key)
    {
        Validate(mode);
        if (supplied is null) return existing;
        if (mode == "replace") return supplied;
        if (supplied.Any(item => item is null || string.IsNullOrWhiteSpace(key(item))))
            throw McpToolErrors.ValidationError("Each link must have a non-empty handle/ref.");
        if (supplied.Length == 0) return existing;
        var keys = supplied.Select(key).ToHashSet(StringComparer.Ordinal);
        if (mode == "remove")
            return existing?.Where(item => !keys.Contains(key(item))).ToArray() ?? [];
        var result = new List<T>(existing ?? []);
        var seen = result.Select(key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in supplied)
            if (seen.Add(key(item))) result.Add(item);
        return result.ToArray();
    }
}
