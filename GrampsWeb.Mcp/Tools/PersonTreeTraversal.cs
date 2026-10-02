using System.Text.Json;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// Client-side ancestor/descendant walks. Gramps Web API exposes people and families, not
/// <c>/people/{{handle}}/ancestors</c> or <c>/descendants</c> routes. Each generation is loaded with one
/// batch of families and one batch of people; people, the root included, carry <c>profile=self</c> so rows need no event reads.
/// </summary>
internal static class PersonTreeTraversal
{
    private const string RowQuery = "profile=self";

    // Backlinks find parent families missing from parent_family_list, for people whose parents are walked next.
    private const string RowQueryWithBacklinks = "profile=self&backlinks=true";

    public static async Task<PersonTree?> CollectAncestorsAsync(
        GrampsApiClient client,
        string rootHandle,
        int generations)
    {
        var root = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{rootHandle}?{RowQuery}");
        if (root is null)
            return null;

        var familyCache = new Dictionary<string, GrampsFamily>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootHandle };
        var rows = new List<PersonTreeRow>();
        var level = new List<(GrampsPerson Person, List<bool> Path)> { (root, []) };

        for (var gen = 1; gen <= generations && level.Count > 0; gen++)
        {
            var parentFamilies = await GetNatalParentFamiliesAsync(client, [.. level.Select(x => x.Person)], familyCache);

            var parents = new List<(string Handle, List<bool> Path)>();
            for (var i = 0; i < level.Count; i++)
            {
                foreach (var family in parentFamilies[i])
                {
                    var path = level[i].Path;
                    AddParent(parents, family.FatherHandle, path, viaFather: true);
                    AddParent(parents, family.MotherHandle, path, viaFather: false);
                }
            }

            // Pedigree collapse: the first path to an ancestor wins.
            var next = parents.Where(p => visited.Add(p.Handle)).ToList();
            var people = await client.GetByHandlesAsync<GrampsPerson>(
                "people", next.Select(p => p.Handle), p => p.Handle,
                gen < generations ? RowQueryWithBacklinks : RowQuery);

            level = next
                .Where(p => people.ContainsKey(p.Handle))
                .Select(p => (people[p.Handle], p.Path))
                .ToList();
            rows.AddRange(level.Select(x => new PersonTreeRow(x.Person, gen, x.Path)));
        }

        return new PersonTree(root, rows.ToArray());
    }

    private static void AddParent(List<(string Handle, List<bool> Path)> parents, string? handle, List<bool> path, bool viaFather)
    {
        if (!string.IsNullOrEmpty(handle))
            parents.Add((handle, [.. path, viaFather]));
    }

    /// <summary>
    /// Parent (natal) families of each person, in input order: <see cref="GrampsPerson.ParentFamilyList"/>, or
    /// families from backlinks that list the person in <c>child_ref_list</c> (covers some DB inconsistencies).
    /// Spouse families reference the person as father/mother, not as child — those are excluded.
    /// </summary>
    private static async Task<List<GrampsFamily>[]> GetNatalParentFamiliesAsync(
        GrampsApiClient client,
        IReadOnlyList<GrampsPerson> people,
        Dictionary<string, GrampsFamily> familyCache)
    {
        var candidates = new (string[] FamilyHandles, bool ViaBacklinks)[people.Count];
        for (var i = 0; i < people.Count; i++)
        {
            var listed = DistinctHandles(people[i].ParentFamilyList?.Select(r => r.Ref));
            candidates[i] = listed.Length > 0 || string.IsNullOrEmpty(people[i].Handle)
                ? (listed, false)
                : (await GetBacklinkFamilyHandlesAsync(client, people[i]), true);
        }

        var missing = candidates.SelectMany(c => c.FamilyHandles).Where(h => !familyCache.ContainsKey(h));
        foreach (var (handle, family) in await client.GetByHandlesAsync<GrampsFamily>("families", missing, f => f.Handle))
            familyCache[handle] = family;

        return [.. people.Select((person, i) => candidates[i].FamilyHandles
            .Where(familyCache.ContainsKey)
            .Select(h => familyCache[h])
            .Where(f => !candidates[i].ViaBacklinks || (f.ChildRefList ?? [])
                .Any(c => string.Equals(c.Ref, person.Handle, StringComparison.Ordinal)))
            .ToList())];
    }

    /// <summary>Families referring to the person, from the batch's backlinks or, for the root, one more read.</summary>
    private static async Task<string[]> GetBacklinkFamilyHandlesAsync(GrampsApiClient client, GrampsPerson person)
    {
        if (person.Backlinks is { } backlinks)
            return DistinctHandles(backlinks.Family);

        var raw = await client.GetJsonOrNullIfNotFoundAsync($"/api/people/{person.Handle}?backlinks=true");
        if (raw is not { ValueKind: JsonValueKind.Object } doc
            || !doc.TryGetProperty("backlinks", out var links) || links.ValueKind != JsonValueKind.Object
            || !links.TryGetProperty("family", out var families) || families.ValueKind != JsonValueKind.Array)
            return [];

        return DistinctHandles(families.EnumerateArray()
            .Where(el => el.ValueKind == JsonValueKind.String)
            .Select(el => el.GetString()));
    }

    private static string[] DistinctHandles(IEnumerable<string?>? handles) =>
        (handles ?? [])
        .Where(h => !string.IsNullOrEmpty(h))
        .Select(h => h!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static async Task<PersonTree?> CollectDescendantsAsync(
        GrampsApiClient client,
        string rootHandle,
        int generations)
    {
        var root = await client.GetOrNullIfNotFoundAsync<GrampsPerson>($"/api/people/{rootHandle}?{RowQuery}");
        if (root is null)
            return null;

        var visited = new HashSet<string>(StringComparer.Ordinal) { rootHandle };
        var rows = new List<PersonTreeRow>();
        var level = new List<GrampsPerson> { root };

        for (var gen = 1; gen <= generations && level.Count > 0; gen++)
        {
            var familyHandles = level.SelectMany(p => DistinctHandles(p.FamilyList)).ToList();
            var families = await client.GetByHandlesAsync<GrampsFamily>("families", familyHandles, f => f.Handle);

            var next = familyHandles
                .Where(families.ContainsKey)
                .SelectMany(h => families[h].ChildRefList ?? [])
                .Select(c => c.Ref)
                .Where(h => !string.IsNullOrEmpty(h) && visited.Add(h))
                .Select(h => h!)
                .ToList();
            var people = await client.GetByHandlesAsync<GrampsPerson>("people", next, p => p.Handle, RowQuery);

            level = next.Where(people.ContainsKey).Select(h => people[h]).ToList();
            rows.AddRange(level.Select(p => new PersonTreeRow(p, gen, null)));
        }

        return new PersonTree(root, rows.ToArray());
    }
}
