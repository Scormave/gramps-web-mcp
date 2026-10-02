using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Formatters;

/// <summary>
/// Names the objects a card links to, so its sections read <c>• [General] Born at home [handle: …]</c>
/// instead of a bare handle.
/// </summary>
public static class LinkedObjectLabels
{
    /// <summary>Objects named per collection; past this many, a long backlink list keeps bare handles.</summary>
    internal const int MaxPerCollection = 200;

    /// <summary>
    /// The label of each linked object by handle: its search result line without the kind. Objects of one
    /// collection are read in one batch request, collections in parallel. An object that could not be read
    /// has no label, and its handle stays bare.
    /// </summary>
    /// <param name="links">Linked handles by collection, such as <c>("notes", note.TagList)</c>.</param>
    /// <param name="backlinks">The objects that reference the card's object.</param>
    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        GrampsApiClient client,
        IEnumerable<(string Collection, IEnumerable<string>? Handles)> links,
        IReadOnlyList<BacklinkGroup>? backlinks = null)
    {
        var all = links.Concat((backlinks ?? []).Select(g => (Collection: g.Key, Handles: (IEnumerable<string>?)g.Handles)));
        var wanted = all
            .Where(link => link.Handles != null && SearchFormatter.CollectionOf(link.Collection) != null)
            .GroupBy(link => SearchFormatter.CollectionOf(link.Collection)!, StringComparer.Ordinal)
            .Select(group => (Collection: group.Key, Handles: group
                .SelectMany(link => link.Handles!)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(MaxPerCollection)
                .ToList()))
            .Where(group => group.Handles.Count > 0)
            .ToList();
        if (wanted.Count == 0)
            return new Dictionary<string, string>();

        var tablesTask = GrampsDefaultTypeLabels.PrefetchForSearchAsync(wanted.Select(group => group.Collection), client);
        var objectsTask = Task.WhenAll(wanted.Select(async group =>
        {
            try
            {
                return await SearchFormatter.FetchByHandlesAsync(group.Collection, group.Handles, client);
            }
            catch
            {
                return new Dictionary<string, object>(); // These handles stay bare.
            }
        }));
        await Task.WhenAll(tablesTask, objectsTask);
        var tables = await tablesTask;

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var objects in await objectsTask)
        {
            foreach (var (handle, item) in objects)
            {
                if (SearchFormatter.FormatSummary(item, tables)?.Trim() is { Length: > 0 } label)
                    labels[handle] = label;
            }
        }

        return labels;
    }
}
