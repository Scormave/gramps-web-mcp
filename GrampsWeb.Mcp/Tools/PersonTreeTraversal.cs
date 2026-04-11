using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Client-side ancestor/descendant walks. Gramps Web API exposes people and families, not
/// <c>/people/{{handle}}/ancestors</c> or <c>/descendants</c> routes.
/// </summary>
internal static class PersonTreeTraversal
{
    public static async Task<PersonTreeRow[]?> CollectAncestorsAsync(
        GrampsApiClient client,
        string rootHandle,
        int generations)
    {
        var root = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{rootHandle}");
        if (root is null)
            return null;

        var personCache = new Dictionary<string, GrampsPerson>(StringComparer.Ordinal)
        {
            [rootHandle] = root
        };
        var familyCache = new Dictionary<string, GrampsFamily?>(StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<PersonTreeRow>();
        var queue = new Queue<(string Handle, int Gen, List<bool> Path)>();
        queue.Enqueue((rootHandle, 0, new List<bool>()));

        while (queue.Count > 0)
        {
            var (handle, gen, path) = queue.Dequeue();
            if (visited.Contains(handle))
                continue;
            visited.Add(handle);

            var person = await GetPersonAsync(client, personCache, handle);
            if (person is null)
                continue;

            if (gen >= 1 && gen <= generations)
                ordered.Add(new PersonTreeRow(person, gen, new List<bool>(path)));

            if (gen >= generations)
                continue;

            foreach (var familyHandle in await GetNatalParentFamilyHandlesAsync(client, person, familyCache))
            {
                var family = await GetFamilyAsync(client, familyCache, familyHandle);
                if (family is null)
                    continue;

                EnqueueAncestorParent(queue, family.FatherHandle, gen + 1, path, viaFather: true);
                EnqueueAncestorParent(queue, family.MotherHandle, gen + 1, path, viaFather: false);
            }
        }

        return ordered.ToArray();
    }

    private static void EnqueueAncestorParent(
        Queue<(string Handle, int Gen, List<bool> Path)> queue,
        string? handle,
        int nextGen,
        List<bool> path,
        bool viaFather)
    {
        if (string.IsNullOrEmpty(handle))
            return;
        var nextPath = new List<bool>(path) { viaFather };
        queue.Enqueue((handle, nextGen, nextPath));
    }

    /// <summary>
    /// Parent (natal) families: <see cref="GrampsPerson.ParentFamilyList"/>, or families from
    /// <c>?backlinks=true</c> that list this person in <c>child_ref_list</c> (covers some DB inconsistencies).
    /// Spouse families reference the person as father/mother, not as child — those are excluded.
    /// </summary>
    private static async Task<List<string>> GetNatalParentFamilyHandlesAsync(
        GrampsApiClient client,
        GrampsPerson person,
        Dictionary<string, GrampsFamily?> familyCache)
    {
        var handles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string? h)
        {
            if (string.IsNullOrEmpty(h) || !seen.Add(h))
                return;
            handles.Add(h);
        }

        if (person.ParentFamilyList is { Length: > 0 })
        {
            foreach (var pref in person.ParentFamilyList)
                TryAdd(pref.Ref);
        }

        if (handles.Count > 0 || string.IsNullOrEmpty(person.Handle))
            return handles;

        var raw = await client.GetJsonOrNullIfNotFoundAsync($"/api/people/{person.Handle}?backlinks=true");
        if (raw is not { } doc || doc.ValueKind != JsonValueKind.Object)
            return handles;

        if (!doc.TryGetProperty("backlinks", out var backlinks) || backlinks.ValueKind != JsonValueKind.Object)
            return handles;

        if (!backlinks.TryGetProperty("family", out var famArr) || famArr.ValueKind != JsonValueKind.Array)
            return handles;

        foreach (var el in famArr.EnumerateArray())
        {
            var fh = el.GetString();
            if (string.IsNullOrEmpty(fh))
                continue;

            var family = await GetFamilyAsync(client, familyCache, fh);
            if (family?.ChildRefList is not { Length: > 0 })
                continue;

            foreach (var cref in family.ChildRefList)
            {
                if (string.Equals(cref.Ref, person.Handle, StringComparison.Ordinal))
                {
                    TryAdd(fh);
                    break;
                }
            }
        }

        return handles;
    }

    public static async Task<PersonTreeRow[]?> CollectDescendantsAsync(
        GrampsApiClient client,
        string rootHandle,
        int generations)
    {
        var root = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{rootHandle}");
        if (root is null)
            return null;

        var personCache = new Dictionary<string, GrampsPerson>(StringComparer.Ordinal)
        {
            [rootHandle] = root
        };
        var familyCache = new Dictionary<string, GrampsFamily?>(StringComparer.Ordinal);

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<PersonTreeRow>();
        var queue = new Queue<(string Handle, int Gen)>();
        queue.Enqueue((rootHandle, 0));

        while (queue.Count > 0)
        {
            var (handle, gen) = queue.Dequeue();
            if (visited.Contains(handle))
                continue;
            visited.Add(handle);

            var person = await GetPersonAsync(client, personCache, handle);
            if (person is null)
                continue;

            if (gen >= 1 && gen <= generations)
                ordered.Add(new PersonTreeRow(person, gen, null));

            if (gen >= generations)
                continue;

            if (person.FamilyList is not { Length: > 0 })
                continue;

            foreach (var familyHandle in person.FamilyList)
            {
                if (string.IsNullOrEmpty(familyHandle))
                    continue;

                var family = await GetFamilyAsync(client, familyCache, familyHandle);
                if (family?.ChildRefList is not { Length: > 0 })
                    continue;

                foreach (var cref in family.ChildRefList)
                {
                    if (!string.IsNullOrEmpty(cref.Ref))
                        queue.Enqueue((cref.Ref, gen + 1));
                }
            }
        }

        return ordered.ToArray();
    }

    private static async Task<GrampsPerson?> GetPersonAsync(
        GrampsApiClient client,
        Dictionary<string, GrampsPerson> cache,
        string handle)
    {
        if (cache.TryGetValue(handle, out var existing))
            return existing;

        var p = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{handle}");
        if (p is not null)
            cache[handle] = p;
        return p;
    }

    private static async Task<GrampsFamily?> GetFamilyAsync(
        GrampsApiClient client,
        Dictionary<string, GrampsFamily?> cache,
        string handle)
    {
        if (cache.TryGetValue(handle, out var existing))
            return existing;

        var f = await client.GetOrNullIfNotFoundAsync<GrampsFamily>($"/api/families/{handle}");
        cache[handle] = f;
        return f;
    }
}
