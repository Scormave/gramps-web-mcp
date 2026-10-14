using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Dates;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;

namespace GrampsWeb.Mcp.Tools;

/// <summary>The link lists of a person or family that the <c>sort</c> argument of the update tools can order.</summary>
[Flags]
public enum SortedLists
{
    None = 0,
    Events = 1,
    Children = 2,
    Families = 4,
    Citations = 8,
    Media = 16
}

/// <summary>
/// Puts link lists of a person or family in date order (the <c>sort</c> argument of the update tools). The stored
/// entries are only moved, so their roles, relations, crops, notes, citations and privacy stay. Entries with equal
/// dates keep their stored order, and undated ones go last.
/// </summary>
internal static class LinkOrder
{
    public const SortedLists PersonLists = SortedLists.Events | SortedLists.Families | SortedLists.Citations | SortedLists.Media;

    public const SortedLists FamilyLists = SortedLists.Events | SortedLists.Children | SortedLists.Citations | SortedLists.Media;

    private const string Usage =
        "Lists to put in date order after the other changes: " + FlexibleSortList.DescriptionHint + " " +
        "Links are only moved, keeping their roles and other metadata; alone it sorts the stored lists. " +
        "Omit to keep the order (linkMode add appends at the end). ";

    public const string PersonDescription =
        Usage + "Lists: Events, Families, Citations, Media. " + EventRefOrder.Order + " " +
        "Families (this person's marriages and other unions): by the Marriage date, else another marriage event " +
        "(Engagement, Marriage Banns, …), else the earliest birth of a child. Citations and Media: by their own date; " +
        "Gramps shows the first media as the person's picture, so sorting Media can change it. " +
        "Undated families, citations and media go last; equal dates keep their order.";

    public const string FamilyDescription =
        Usage + "Lists: Events, Children, Citations, Media. " + EventRefOrder.Order + " " +
        "Children: by birth date, else baptism or christening date. Citations and Media: by their own date. " +
        "Undated children, citations and media go last; equal dates keep their order.";

    private static readonly string[] MarriageFallbackTypes =
    [
        "Engagement", "Alternate Marriage", "Marriage Banns", "Marriage License", "Marriage Contract",
        "Marriage Settlement"
    ];

    /// <summary>The lists named by <paramref name="sort"/>; a name outside <paramref name="allowed"/> is rejected.</summary>
    public static SortedLists Parse(FlexibleSortList? sort, SortedLists allowed)
    {
        var lists = SortedLists.None;
        foreach (var name in sort?.Names ?? [])
        {
            var list = Enum.GetValues<SortedLists>().FirstOrDefault(l =>
                l != SortedLists.None && allowed.HasFlag(l)
                && string.Equals(l.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (list == SortedLists.None)
                throw McpToolErrors.ValidationError(
                    $"sort: \"{name}\" is not a list this tool sorts; use {string.Join(", ", NamesOf(allowed))}.");
            lists |= list;
        }

        return lists;
    }

    /// <summary>Sorts the named lists of <paramref name="root"/> in place.</summary>
    public static async Task SortAsync(GrampsApiClient client, JsonObject root, SortedLists lists)
    {
        if (lists.HasFlag(SortedLists.Events))
            await EventRefOrder.SortAsync(client, root);
        if (lists.HasFlag(SortedLists.Children))
            await SortChildrenAsync(client, root);
        if (lists.HasFlag(SortedLists.Families))
            await SortFamiliesAsync(client, root);
        if (lists.HasFlag(SortedLists.Citations))
            await SortByOwnDateAsync<GrampsCitation>(client, root, "citation_list", "citations", c => c.Handle, c => c.Date);
        if (lists.HasFlag(SortedLists.Media))
            await SortByOwnDateAsync<GrampsMedia>(client, root, "media_list", "media", m => m.Handle, m => m.Date);
    }

    /// <summary>
    /// Like Gramps <c>get_birth_or_fallback</c>, but only dated events count: the birth of record, else the first
    /// Birth, Baptism or Christening the person has the Primary role in. Null when none has a date.
    /// </summary>
    internal static int? BirthSortVal(GrampsPersonExtended person)
    {
        var events = EventsByHandle(person.Extended?.Events);
        var refs = person.EventRefList ?? [];
        var candidates = new List<GrampsEventRef>();
        if (person.BirthRefIndex is { } index && index >= 0 && index < refs.Length)
            candidates.Add(refs[index]);
        var primary = refs.Where(r => PersonFormatter.IsPrimaryRole(r.Role)).ToArray();
        candidates.AddRange(primary.Where(r => IsType(EventOf(r, events), "Birth")));
        candidates.AddRange(primary.Where(r => IsType(EventOf(r, events), "Baptism", "Christening")));
        return candidates.Select(r => GrampsDateSortVal.TryGetDatedSortKey(EventOf(r, events)?.Date))
            .FirstOrDefault(d => d is not null);
    }

    /// <summary>
    /// Like Gramps <c>get_marriage_or_fallback</c>, but only dated events count: the first Marriage the family has
    /// the Family or Primary role in, else the first other marriage event. Null when none has a date.
    /// </summary>
    internal static int? MarriageSortVal(GrampsFamilyExtended family)
    {
        var events = EventsByHandle(family.Extended?.Events);
        var own = (family.EventRefList ?? [])
            .Where(r => PersonFormatter.IsPrimaryRole(r.Role)
                        || string.Equals(r.Role?.Trim(), "Family", StringComparison.OrdinalIgnoreCase))
            .Select(r => EventOf(r, events))
            .ToArray();
        return own.Where(e => IsType(e, "Marriage")).Concat(own.Where(e => IsType(e, MarriageFallbackTypes)))
            .Select(e => GrampsDateSortVal.TryGetDatedSortKey(e?.Date))
            .FirstOrDefault(d => d is not null);
    }

    internal static string? RefHandle(JsonNode? node) =>
        GrampsObjectPatch.StringValue(node is JsonObject o ? o["ref"] : node)?.Trim();

    private static async Task SortChildrenAsync(GrampsApiClient client, JsonObject root)
    {
        if (root["child_ref_list"] is not JsonArray { Count: > 1 } list)
            return;
        var births = await BirthSortValsAsync(client, list.Select(RefHandle));
        Reorder(root, "child_ref_list", h => births.GetValueOrDefault(h));
    }

    private static async Task SortFamiliesAsync(GrampsApiClient client, JsonObject root)
    {
        if (root["family_list"] is not JsonArray { Count: > 1 } list)
            return;
        var handles = list.Select(RefHandle).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var families = await client.GetByHandlesAsync<GrampsFamilyExtended>(
            "families", handles, f => f.Handle, query: "extend=event_ref_list");
        var stored = handles.Where(families.ContainsKey).Select(h => (Handle: h, Family: families[h])).ToArray();
        var dates = stored.ToDictionary(f => f.Handle, f => MarriageSortVal(f.Family), StringComparer.Ordinal);

        // A family without a dated marriage takes the date of its first child.
        var undated = stored.Where(f => dates[f.Handle] is null).ToArray();
        if (undated.Length > 0)
        {
            var births = await BirthSortValsAsync(client,
                undated.SelectMany(f => f.Family.ChildRefList ?? []).Select(c => c.Ref?.Trim()));
            foreach (var (handle, family) in undated)
                dates[handle] = (family.ChildRefList ?? [])
                    .Select(c => c.Ref?.Trim() is { } child ? births.GetValueOrDefault(child) : null)
                    .Min();
        }

        Reorder(root, "family_list", h => dates.GetValueOrDefault(h));
    }

    private static async Task SortByOwnDateAsync<T>(
        GrampsApiClient client,
        JsonObject root,
        string key,
        string collection,
        Func<T, string?> handleOf,
        Func<T, GrampsDate?> dateOf)
        where T : class
    {
        if (root[key] is not JsonArray { Count: > 1 } list)
            return;
        var objects = await client.GetByHandlesAsync(collection, list.Select(RefHandle), handleOf);
        Reorder(root, key, h => objects.GetValueOrDefault(h) is { } obj
            ? GrampsDateSortVal.TryGetDatedSortKey(dateOf(obj))
            : null);
    }

    /// <summary>Birth sort values by person handle, from one batch read; people without one are left out.</summary>
    private static async Task<Dictionary<string, int?>> BirthSortValsAsync(GrampsApiClient client, IEnumerable<string?> handles)
    {
        var people = await client.GetByHandlesAsync<GrampsPersonExtended>(
            "people", handles, p => p.Handle, query: "extend=event_ref_list");
        return people
            .Select(p => (p.Key, Date: BirthSortVal(p.Value)))
            .Where(p => p.Date is not null)
            .ToDictionary(p => p.Key, p => p.Date, StringComparer.Ordinal);
    }

    /// <summary>Orders <c>root[key]</c> by the dates of its entries' handles; OrderBy is stable, so ties keep their order.</summary>
    private static void Reorder(JsonObject root, string key, Func<string, int?> dateOf)
    {
        var entries = ((JsonArray)root[key]!).ToArray();
        var keys = entries
            .Select(e => RefHandle(e) is { } h && dateOf(h) is { } date ? (Undated: 0, Date: date) : (Undated: 1, Date: 0))
            .ToArray();
        var order = Enumerable.Range(0, entries.Length).OrderBy(i => keys[i]).ToArray();
        root[key] = new JsonArray(order.Select(i => entries[i]?.DeepClone()).ToArray());
    }

    private static Dictionary<string, GrampsEvent> EventsByHandle(IEnumerable<GrampsEvent>? events) =>
        (events ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Handle))
            .GroupBy(e => e.Handle!.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static GrampsEvent? EventOf(GrampsEventRef eventRef, Dictionary<string, GrampsEvent> events) =>
        eventRef.Ref?.Trim() is { } h ? events.GetValueOrDefault(h) : null;

    private static bool IsType(GrampsEvent? evt, params string[] types) =>
        evt?.Type?.Trim() is { } type && types.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<SortedLists> NamesOf(SortedLists allowed) =>
        Enum.GetValues<SortedLists>().Where(l => l != SortedLists.None && allowed.HasFlag(l));
}
