using System.ComponentModel;
using System.Text;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using ModelContextProtocol.Server;
using static GrampsWeb.Mcp.Tools.DuplicatePeople;
using static GrampsWeb.Mcp.Tools.PersonNameMatching;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// MCP tool that looks for people entered twice across the whole tree. It reads every person with their profile,
/// pairs the people where the first word of one's given name is a word of the other's, and compares each pair with
/// the rule of create_records' duplicate check (<see cref="DuplicatePeople.Compare"/>); spouses and ancestors are
/// left out.
/// </summary>
[McpServerToolType]
public static class DuplicateTools
{
    /// <summary>People one request of the scan reads.</summary>
    internal const int PageSize = 500;

    /// <summary>Most people one scan reads.</summary>
    internal const int MaxPeople = 20000;

    private const string ScanKeys =
        "handle,gramps_id,gender,primary_name,alternate_names,parent_family_list,family_list,profile";

    [McpServerTool(Title = "Find Duplicates", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description(
        "Find people entered twice in the tree: pairs with the same given name (the first word of one's among the " +
        "other's, so Мария pairs with Анна Мария) and surname in any spelling (Cyrillic or Latin letters, so Мария, " +
        "Марья and Maria, or Kowalski written in Cyrillic; ё and pre-reform letters, Latin accents, Polish w, sz and " +
        "cz) and in the male, female or daughter's form (Kowalski and Kowalska, Novák and Nováková, Hájek and " +
        "Hájková, Kazlauskas, Kazlauskienė and Kazlauskaitė, Ozols and Ozola, Papadopoulos and Papadopoulou, Kovács " +
        "and Kovácsné), not of the other gender, without different patronymics, born and dead at most five years " +
        "apart, and neither dead before the other was born. " +
        "People with a patronymic but no surname pair by given name and patronymic when their birth years or parents " +
        "can be compared. Spouses, and a person and their parent, grandparent or earlier ancestor, are never paired. " +
        "Pairs born at most two years apart, or children of the same parents without two years to compare, are " +
        "possible duplicates; the others are similar pairs. Each call reads every person of the tree, up to 20000, " +
        "so a tree of a few thousand people takes 10 to 20 seconds. Returns how many people were compared and the " +
        "pairs, possible duplicates first and then by how close the births are, numbered across pages; each person " +
        "with Gramps ID, handle, birth, death and parents. Nothing changes: compare a pair with get_object, and merge " +
        "it with merge_objects only after the user confirmed they are one person. create_records runs the same " +
        "check on the people it creates; to find one name, use search.")]
    public static Task<string> FindDuplicates(
        [Description("Only pairs with this person. " + ToolDescriptionFragments.HandleDiscovery)]
        string? personHandle = null,
        [Description("Only pairs where one of the people has this surname, in any spelling or form: Kowalski also " +
                     "finds Kowalska and Kowalski in Cyrillic letters, and Novák also finds Nováková.")]
        string? surname = null,
        [Description("How many pairs to show, 1–100 (default 20); other values are clamped.")]
        int limit = 20,
        [Description("How many pairs to skip, to show the next ones (default 0).")]
        int offset = 0,
        GrampsApiClient client = null!) =>
        FindDuplicatesAsync(personHandle, surname, limit, offset, client);

    internal static async Task<string> FindDuplicatesAsync(
        string? personHandle, string? surname, int limit, int offset, GrampsApiClient client)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 100);
            offset = Math.Max(offset, 0);
            IReadOnlySet<string>? stems = null;
            if (!string.IsNullOrWhiteSpace(surname) && (stems = SurnameStems(surname)).Count == 0)
                throw McpToolErrors.ValidationError($"surname needs at least two letters: \"{surname.Trim()}\".");
            var handle = string.IsNullOrWhiteSpace(personHandle)
                ? null
                : await HandleResolver.ResolveToHandleAsync(personHandle.Trim(), client, "people");

            var (people, total) = await ReadPeopleAsync(client);
            var person = handle == null ? null : people.FirstOrDefault(p => p.Handle?.Trim() == handle);
            if (handle != null && person == null)
                throw McpToolErrors.ValidationError(NotFoundHelper.NotFoundMessage("Person", personHandle!.Trim()));

            var scan = Scan(people, handle, stems);
            return Format(scan, people, total, person, surname?.Trim(), limit, offset);
        }
        catch (Exception ex)
        {
            throw McpToolErrors.ToMcpException(ex);
        }
    }

    /// <summary>Every person of the tree with their profile, up to <see cref="MaxPeople"/>, and how many there are.</summary>
    private static async Task<(IReadOnlyList<GrampsPerson> People, int Total)> ReadPeopleAsync(GrampsApiClient client)
    {
        var people = new List<GrampsPerson>();
        var handles = new HashSet<string>(StringComparer.Ordinal);
        var total = -1;
        for (var page = 1; people.Count < MaxPeople; page++)
        {
            var result = await client.GetPagedListAsync<GrampsPerson>(
                $"/api/people/?keys={ScanKeys}&profile=self&page={page}&pagesize={PageSize}");
            var objects = result.Objects ?? [];
            total = Math.Max(total, result.Total);
            // An edit between two pages can shift a person onto the next one.
            people.AddRange(objects.Where(p => !string.IsNullOrWhiteSpace(p.Handle) && handles.Add(p.Handle.Trim()))
                .Take(MaxPeople - people.Count));
            if (objects.Length < PageSize || total >= 0 && page * PageSize >= total)
                break;
        }

        return (people, Math.Max(total, people.Count));
    }

    /// <summary>A pair of people who may be one, in Gramps ID order.</summary>
    internal sealed record Pair(GrampsPerson First, GrampsPerson Second, Match Match);

    /// <summary>
    /// The pairs, possible duplicates first; <paramref name="Compared"/> people had names to compare and
    /// <paramref name="Skipped"/> had no given name, or neither a surname nor a patronymic.
    /// </summary>
    internal sealed record ScanResult(IReadOnlyList<Pair> Pairs, int Compared, int Skipped)
    {
        public int Strong => Pairs.Count(p => p.Match.Level == Level.Strong);
    }

    private sealed record Entry(
        GrampsPerson Person, string Handle, Side Side, IReadOnlySet<string> ParentFamilies, IReadOnlySet<string> Families,
        IReadOnlySet<string> Parents);

    /// <summary>
    /// Compares every two people where the first word of one's given name is a word of the other's.
    /// <paramref name="personHandle"/> keeps the pairs with that person, <paramref name="surnameStems"/> the pairs where
    /// one of the people has one of those surname stems.
    /// </summary>
    internal static ScanResult Scan(
        IEnumerable<GrampsPerson> people, string? personHandle = null, IReadOnlySet<string>? surnameStems = null)
    {
        var all = people.Where(p => !string.IsNullOrWhiteSpace(p.Handle)).ToList();

        // A family's parents are the people who list it among their own families.
        var familyParents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var person in all)
        foreach (var family in Handles(person.FamilyList))
            (familyParents.TryGetValue(family, out var list) ? list : familyParents[family] = []).Add(person.Handle!.Trim());
        var parentsOf = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var person in all)
            parentsOf.TryAdd(person.Handle!.Trim(), Handles((person.ParentFamilyList ?? []).Select(f => f.Ref))
                .SelectMany(f => familyParents.GetValueOrDefault(f) ?? []).ToHashSet(StringComparer.Ordinal));

        var entries = new List<Entry>();
        var skipped = 0;
        foreach (var person in all)
        {
            var side = SideOf(person);
            if (side.Names.Given.Count == 0 || side.Names.SurnameStems.Count == 0 && side.Names.PatronymicRoots.Count == 0)
            {
                skipped++;
                continue;
            }

            var handle = person.Handle!.Trim();
            entries.Add(new Entry(person, handle, side, Handles((person.ParentFamilyList ?? []).Select(f => f.Ref)),
                Handles(person.FamilyList), parentsOf[handle]));
        }

        var byGiven = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        foreach (var given in entries[i].Side.Names.Given)
            (byGiven.TryGetValue(given, out var list) ? list : byGiven[given] = []).Add(i);

        var seen = new HashSet<(int, int)>();
        var pairs = new List<Pair>();
        for (var i = 0; i < entries.Count; i++)
        foreach (var first in entries[i].Side.Names.FirstGiven)
        foreach (var j in byGiven.GetValueOrDefault(first) ?? [])
        {
            var (a, b) = (entries[i], entries[j]);
            if (i == j
                || personHandle != null && a.Handle != personHandle && b.Handle != personHandle
                || surnameStems != null && !a.Side.Names.SurnameStems.Overlaps(surnameStems)
                                        && !b.Side.Names.SurnameStems.Overlaps(surnameStems)
                || !seen.Add((Math.Min(i, j), Math.Max(i, j))))
                continue;

            var sameParents = a.ParentFamilies.Overlaps(b.ParentFamilies) || a.Parents.Overlaps(b.Parents);
            if (Compare(a.Side, b.Side, sameParents) is not { } match || Related(a, b, parentsOf))
                continue;
            pairs.Add(Order(a.Person, b.Person) <= 0 ? new Pair(a.Person, b.Person, match) : new Pair(b.Person, a.Person, match));
        }

        var sorted = pairs.OrderBy(p => p.Match.Level).ThenBy(p => p.Match.YearGap ?? int.MaxValue)
            .ThenBy(p => p.First, Comparer<GrampsPerson>.Create(Order))
            .ThenBy(p => p.Second, Comparer<GrampsPerson>.Create(Order))
            .ToList();
        return new ScanResult(sorted, entries.Count, skipped);
    }

    /// <summary>
    /// Spouses, which Gramps refuses to merge, or one an ancestor of the other: a parent and their child, which Gramps
    /// refuses too, or a namesake grandfather.
    /// </summary>
    private static bool Related(Entry a, Entry b, IReadOnlyDictionary<string, IReadOnlySet<string>> parentsOf) =>
        a.Families.Overlaps(b.Families) || IsAncestor(a.Handle, b.Handle, parentsOf) || IsAncestor(b.Handle, a.Handle, parentsOf);

    /// <summary>Whether <paramref name="ancestor"/> is a parent, grandparent or earlier ancestor of <paramref name="person"/>.</summary>
    private static bool IsAncestor(string ancestor, string person, IReadOnlyDictionary<string, IReadOnlySet<string>> parentsOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { person };
        var queue = new Queue<string>([person]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var parent in parentsOf.GetValueOrDefault(current) ?? new HashSet<string>())
            {
                if (parent == ancestor)
                    return true;
                if (seen.Add(parent))
                    queue.Enqueue(parent);
            }
        }

        return false;
    }

    /// <summary>By Gramps ID, then handle.</summary>
    private static int Order(GrampsPerson a, GrampsPerson b)
    {
        var byId = string.CompareOrdinal(a.GrampsId?.Trim() ?? "", b.GrampsId?.Trim() ?? "");
        return byId != 0 ? byId : string.CompareOrdinal(a.Handle, b.Handle);
    }

    private static IReadOnlySet<string> Handles(IEnumerable<string?>? handles) =>
        (handles ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h!.Trim()).ToHashSet(StringComparer.Ordinal);

    /// <summary>The reply: what was compared, the pairs from <paramref name="offset"/>, and how to go on.</summary>
    internal static string Format(ScanResult scan, IReadOnlyList<GrampsPerson> people, int total, GrampsPerson? person,
        string? surname, int limit, int offset)
    {
        var parents = people.SelectMany(p => (p.FamilyList ?? []).Select(f => (Family: f.Trim(), Parent: p)))
            .ToLookup(p => p.Family, p => p.Parent, StringComparer.Ordinal);
        var strong = scan.Strong;
        var weak = scan.Pairs.Count - strong;
        var sb = new StringBuilder();
        sb.AppendLine("POSSIBLE DUPLICATE PEOPLE");
        sb.AppendLine(new string('=', 60));
        sb.Append($"Compared {scan.Compared} {(scan.Compared == 1 ? "person" : "people")}");
        sb.AppendLine(scan.Skipped == 0
            ? "."
            : $"; left out {scan.Skipped} without a given name, or with neither a surname nor a patronymic.");
        if (total > people.Count)
            sb.AppendLine($"Read the first {people.Count} of {total} people; pairs with the others are not shown.");
        if (person != null)
            sb.AppendLine($"Only pairs with {PersonFormatter.FormatRelationPerson(person.Profile, person.Handle!)}.");
        if (surname != null)
            sb.AppendLine($"Only pairs where one of the people has the surname {surname}, in any spelling or form.");

        if (scan.Pairs.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("No possible duplicates or similar pairs.");
            return sb.ToString();
        }

        var shown = scan.Pairs.Skip(offset).Take(limit).ToList();
        sb.AppendLine($"{strong} possible {(strong == 1 ? "duplicate" : "duplicates")} and {weak} similar " +
                      $"{(weak == 1 ? "pair" : "pairs")}" +
                      (shown.Count == 0 ? "." : $"; showing {offset + 1}–{offset + shown.Count}."));
        if (shown.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine($"No pairs from {offset + 1}: call find_duplicates with a smaller offset.");
            return sb.ToString();
        }

        for (var i = 0; i < shown.Count; i++)
        {
            var number = offset + i + 1;
            if (i == 0 || shown[i].Match.Level != shown[i - 1].Match.Level)
            {
                sb.AppendLine();
                sb.AppendLine(shown[i].Match.Level == Level.Strong
                    ? $"Possible duplicates ({strong}):"
                    : $"Similar pairs ({weak}):");
            }

            var reason = shown[i].Match.Reason;
            sb.AppendLine($"{number}. {char.ToUpperInvariant(reason[0])}{reason[1..]}:");
            sb.AppendLine($"   • {FormatPerson(shown[i].First, parents)}");
            sb.AppendLine($"   • {FormatPerson(shown[i].Second, parents)}");
        }

        var first = shown[0];
        sb.AppendLine();
        sb.AppendLine("Next steps:");
        sb.AppendLine($"  • Compare a pair: get_object(objectType: \"person\", identifier: \"{Identifier(first.First)}\", " +
                      $"extended: true), and the same for \"{Identifier(first.Second)}\".");
        sb.AppendLine("  • One person, once the user confirmed it: merge_objects(objectType: \"person\", " +
                      "keepHandle: \"<the record to keep>\", duplicateHandle: \"<the other>\").");
        if (offset + shown.Count < scan.Pairs.Count)
        {
            var filters = (person == null ? "" : $"personHandle: \"{Identifier(person)}\", ") +
                          (surname == null ? "" : $"surname: \"{surname}\", ");
            sb.AppendLine($"  • More pairs: find_duplicates({filters}offset: {offset + shown.Count}).");
        }

        return sb.ToString();
    }

    /// <summary>
    /// "Образцов, Иван (I0012) [handle: h], b. 1864, child of Образцов, Петр (I0010) and Образцова, Анна (I0011)".
    /// </summary>
    private static string FormatPerson(GrampsPerson person, ILookup<string, GrampsPerson> parentsOf)
    {
        var line = PersonFormatter.FormatRelationPerson(person.Profile, person.Handle!);
        var family = person.ParentFamilyList?.FirstOrDefault()?.Ref?.Trim();
        if (family == null)
            return line;

        var parents = parentsOf[family].Distinct()
            .OrderBy(p => p.Gender switch { 1 => 0, 0 => 1, _ => 2 })
            .ThenBy(p => p, Comparer<GrampsPerson>.Create(Order))
            .Select(p => (Name: p.Profile?.NameDisplay?.Trim(), Id: p.GrampsId?.Trim()))
            .Where(p => !string.IsNullOrEmpty(p.Name) || !string.IsNullOrEmpty(p.Id))
            .Select(p => string.IsNullOrEmpty(p.Name) ? p.Id! : string.IsNullOrEmpty(p.Id) ? p.Name : $"{p.Name} ({p.Id})")
            .ToList();
        return parents.Count == 0 ? line : $"{line}, child of {string.Join(" and ", parents)}";
    }

    /// <summary>The Gramps ID to name a person by in next steps, or the handle without one.</summary>
    private static string Identifier(GrampsPerson person) =>
        string.IsNullOrWhiteSpace(person.GrampsId) ? person.Handle!.Trim() : person.GrampsId.Trim();
}
