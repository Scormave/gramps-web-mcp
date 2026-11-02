using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Exceptions;
using GrampsWeb.Mcp.Formatters;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;
using static GrampsWeb.Mcp.Tools.PersonNameMatching;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The duplicate check of create_person and create_records. Gramps Web's RegExpName rule selects the people whose
/// names hold the new person's surname stem and given name in one request (see <see cref="PersonNameMatching"/>);
/// they are then compared here. Anyone of the other gender, with another patronymic or born more than five years
/// apart is dropped. A birth within two years, or the same parents when there are no two years to compare, makes a
/// strong match, which the create tools refuse; the others are reported as similar people.
/// </summary>
internal static partial class DuplicatePeople
{
    /// <summary>Most people one check reads.</summary>
    internal const int PageSize = 100;

    /// <summary>Most similar people a reply lists per new person.</summary>
    internal const int MaxListed = 10;

    /// <summary>Years apart that still make a strong match.</summary>
    private const int StrongYearGap = 2;

    /// <summary>Years apart beyond which a namesake is another person.</summary>
    private const int MaxYearGap = 5;

    /// <summary>Most surname stems and given-name words one search sends.</summary>
    private const int MaxNameParts = 4;

    /// <summary>Longest encoded rules sent: Gunicorn refuses request lines over 4094 bytes.</summary>
    private const int MaxEncodedRules = 3000;

    private const string SearchKeys =
        "handle,gramps_id,gender,primary_name,alternate_names,parent_family_list,profile";

    private static readonly JsonSerializerOptions RulesJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal enum Level { Strong, Weak }

    /// <summary>A person of the tree who may be the new person, and why.</summary>
    internal sealed record Candidate(GrampsPerson Person, Level Level, string Reason, int? YearGap);

    /// <summary>
    /// The candidates, strong first; <paramref name="Matching"/> people matched the search and
    /// <paramref name="Compared"/> were read; <paramref name="Failure"/> says why the check did not run.
    /// </summary>
    internal sealed record CheckResult(IReadOnlyList<Candidate> Candidates, int Matching, int Compared, string? Failure)
    {
        public static readonly CheckResult NotChecked = new([], 0, 0, null);

        public IReadOnlyList<Candidate> Strong => Candidates.Where(c => c.Level == Level.Strong).ToList();

        public IReadOnlyList<Candidate> Weak => Candidates.Where(c => c.Level == Level.Weak).ToList();

        /// <summary>Whether the search matched more people than were read.</summary>
        public bool Partial => Failure == null && Matching > Compared;
    }

    /// <summary>The new person as the check compares them.</summary>
    internal sealed record NewPerson(
        NameKeys Names, int Gender, int? BirthYear, IReadOnlySet<string> ParentFamilies, IReadOnlySet<string> Parents,
        IReadOnlySet<string> NotDuplicateOf);

    /// <summary>The check's view of a person about to be created.</summary>
    /// <param name="birthYear">The year of the birth, or of a baptism without one; null when unknown.</param>
    /// <param name="parents">Existing people who will be the person's father or mother.</param>
    /// <param name="notDuplicateOf">Handles or Gramps IDs of people the caller said are someone else.</param>
    internal static NewPerson Describe(CreatePersonRequest request, int? birthYear, IEnumerable<string?>? parents = null,
        IEnumerable<string?>? notDuplicateOf = null) =>
        new(Keys(request.PrimaryName, request.AlternateNames), request.Gender, birthYear,
            Set(request.ParentFamilyList, StringComparer.Ordinal), Set(parents, StringComparer.Ordinal),
            Set(notDuplicateOf, StringComparer.OrdinalIgnoreCase));

    /// <summary>Reads the people whose names match and compares them; a failed search gives a note, not an error.</summary>
    internal static async Task<CheckResult> CheckAsync(GrampsApiClient client, NewPerson person)
    {
        if (SearchPath(person.Names) is not { } path)
            return CheckResult.NotChecked;

        GrampsPagedResult<GrampsPerson> found;
        try
        {
            found = await client.GetPagedListAsync<GrampsPerson>(path);
        }
        catch (GrampsApiException ex)
        {
            return CheckResult.NotChecked with { Failure = $"HTTP {(int)ex.StatusCode}" };
        }
        catch (JsonException)
        {
            return CheckResult.NotChecked with { Failure = "an answer that could not be read" };
        }

        var people = found.Objects ?? [];
        return new CheckResult(Classify(person, people), Math.Max(found.Total, people.Length), people.Length, null);
    }

    /// <summary>
    /// The people search: RegExpName for one of the surname stems and RegExpName for one of the given-name words.
    /// A search too long for one request line drops the given name, then all stems but one; null when the name
    /// has no surname or no given name to search by.
    /// </summary>
    internal static string? SearchPath(NameKeys names)
    {
        var stems = names.SurnameStems.Order(StringComparer.Ordinal).Take(MaxNameParts).ToList();
        var given = names.Given.Order(StringComparer.Ordinal).Take(MaxNameParts).ToList();
        if (stems.Count == 0 || given.Count == 0)
            return null;

        var rules = Rules(SurnamePattern(stems), GivenPattern(given));
        if (rules.Length > MaxEncodedRules)
            rules = Rules(SurnamePattern(stems));
        if (rules.Length > MaxEncodedRules)
            rules = Rules(SurnamePattern(stems.Take(1)));
        if (rules.Length > MaxEncodedRules)
            return null;
        return $"/api/people/?rules={rules}&keys={SearchKeys}&profile=families&page=1&pagesize={PageSize}";
    }

    private static string Rules(params string[] patterns) => Uri.EscapeDataString(JsonSerializer.Serialize(new
    {
        function = "and",
        rules = patterns.Select(p => new { name = "RegExpName", values = new[] { p }, regex = true })
    }, RulesJson));

    /// <summary>The people who may be the new person, strong first, then by how close the birth years are.</summary>
    internal static IReadOnlyList<Candidate> Classify(NewPerson person, IEnumerable<GrampsPerson> people)
    {
        var candidates = new List<Candidate>();
        foreach (var other in people)
        {
            if (string.IsNullOrWhiteSpace(other.Handle)
                || person.NotDuplicateOf.Contains(other.Handle.Trim())
                || other.GrampsId is { } id && person.NotDuplicateOf.Contains(id.Trim()))
                continue;

            var names = Keys(other.PrimaryName, other.AlternateNames);
            if (!person.Names.SameName(names) || person.Names.OtherPatronymic(names)
                || (person.Gender, other.Gender) is (0, 1) or (1, 0))
                continue;

            int? gap = person.BirthYear is { } year && BirthYear(other.Profile) is { } otherYear
                ? Math.Abs(year - otherYear)
                : null;
            if (gap > MaxYearGap)
                continue;

            var sameParents = SameParents(person, other);
            var strong = gap <= StrongYearGap || sameParents && gap == null;
            candidates.Add(new Candidate(other, strong ? Level.Strong : Level.Weak, Reason(gap, sameParents), gap));
        }

        return candidates.OrderBy(c => c.Level).ThenBy(c => c.YearGap ?? int.MaxValue).ToList();
    }

    /// <summary>The first year in the profile's birth date, which falls back to a baptism; null without one.</summary>
    private static int? BirthYear(GrampsPersonProfile? profile) =>
        YearRegex().Match(profile?.Birth?.Date ?? "") is { Success: true } match ? int.Parse(match.Value) : null;

    /// <summary>Whether the other person is a child of one of the new person's families or of one of the parents.</summary>
    private static bool SameParents(NewPerson person, GrampsPerson other)
    {
        var profile = other.Profile;
        var families = new[] { profile?.PrimaryParentFamily }.Concat(profile?.OtherParentFamilies ?? []).OfType<GrampsFamilyProfile>()
            .ToList();
        if (person.ParentFamilies.Count > 0
            && (other.ParentFamilyList ?? []).Select(f => f.Ref).Concat(families.Select(f => f.Handle))
                .Any(h => h != null && person.ParentFamilies.Contains(h.Trim())))
            return true;
        return person.Parents.Count > 0
               && families.SelectMany(f => new[] { f.Father?.Handle, f.Mother?.Handle })
                   .Any(h => h != null && person.Parents.Contains(h.Trim()));
    }

    private static string Reason(int? gap, bool sameParents)
    {
        var same = sameParents ? "same name and parents" : "same name";
        return gap switch
        {
            null => $"{same}, no birth years to compare",
            0 => $"{same}, born the same year",
            1 => $"{same}, born 1 year apart",
            _ => $"{same}, born {gap} years apart"
        };
    }

    /// <summary>"Petrov, Ivan (I0012) [handle: h], b. 1880, child of Petrov, Petr (I0010) and … — same name, …".</summary>
    internal static string FormatCandidate(Candidate candidate)
    {
        var person = candidate.Person;
        var line = PersonFormatter.FormatRelationPerson(person.Profile, person.Handle!);
        var family = person.Profile?.PrimaryParentFamily;
        var parents = new[] { family?.Father, family?.Mother }
            .Where(p => !string.IsNullOrWhiteSpace(p?.NameDisplay))
            .Select(p => string.IsNullOrWhiteSpace(p!.GrampsId) ? p.NameDisplay!.Trim() : $"{p.NameDisplay!.Trim()} ({p.GrampsId.Trim()})")
            .ToList();
        if (parents.Count > 0)
            line += $", child of {string.Join(" and ", parents)}";
        return $"{line} — {candidate.Reason}";
    }

    /// <summary>The Gramps ID to name a candidate by in next steps, or the handle without one.</summary>
    internal static string Identifier(Candidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.Person.GrampsId) ? candidate.Person.Handle!.Trim() : candidate.Person.GrampsId.Trim();

    /// <summary>Bullet lines for the candidates, at most <see cref="MaxListed"/>, then how many more there are.</summary>
    internal static void AppendCandidates(StringBuilder sb, IReadOnlyList<Candidate> candidates, string indent = "  ")
    {
        foreach (var candidate in candidates.Take(MaxListed))
            sb.AppendLine($"{indent}• {FormatCandidate(candidate)}");
        if (candidates.Count > MaxListed)
            sb.AppendLine($"{indent}… and {candidates.Count - MaxListed} more.");
    }

    /// <summary>A quoted list of identifiers for <c>notDuplicateOf</c>: <c>["I0012", "I0013"]</c>.</summary>
    internal static string IdentifierList(IEnumerable<Candidate> candidates) =>
        $"[{string.Join(", ", candidates.Select(c => $"\"{Identifier(c)}\""))}]";

    /// <summary>
    /// The create_person reply when a strong match stops it: the possible duplicates, the other similar people,
    /// and how to go on either way.
    /// </summary>
    internal static string Refusal(string? name, CheckResult result)
    {
        var strong = result.Strong;
        var weak = result.Weak;
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Person");
        sb.AppendLine("action: refused");
        sb.AppendLine($"name: {ResponseEnvelope.HeaderValue(name)}");
        sb.AppendLine($"duplicates: {strong.Count}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"Did not create {(string.IsNullOrWhiteSpace(name) ? "the person" : name.Trim())}: " +
                      "the tree may already have this person.");
        sb.AppendLine();
        sb.AppendLine($"Possible duplicates ({strong.Count}):");
        AppendCandidates(sb, strong);
        if (weak.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Similar people ({weak.Count}):");
            AppendCandidates(sb, weak);
        }

        if (result.Partial)
        {
            sb.AppendLine();
            sb.AppendLine(PartialNote(result));
        }

        sb.AppendLine();
        sb.AppendLine("Next steps:");
        sb.AppendLine($"  • Compare: get_object(objectType: \"person\", identifier: \"{Identifier(strong[0])}\", extended: true).");
        sb.AppendLine("  • The same person: add the new details to the existing record with update_person instead.");
        sb.AppendLine("  • A different person: call create_person again with the same arguments and " +
                      $"notDuplicateOf: {IdentifierList(strong)}.");
        return sb.ToString();
    }

    /// <summary>
    /// What create_person adds to its reply after creating the person: the similar people with a merge hint,
    /// and a note when the check did not run or did not read every match; empty when there is nothing to say.
    /// </summary>
    internal static string CreatedNote(CheckResult result, string? newHandle)
    {
        var sb = new StringBuilder();
        var weak = result.Weak;
        if (weak.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Similar people already in the tree ({weak.Count}), possibly the same person:");
            AppendCandidates(sb, weak);
            sb.AppendLine("If one of them is this person, compare them with get_object and merge the new record into " +
                          "theirs: merge_objects(objectType: \"person\", keepHandle: \"<their handle>\", " +
                          $"duplicateHandle: \"{newHandle}\").");
        }

        if (result.Failure != null)
        {
            sb.AppendLine();
            sb.AppendLine($"The duplicate check did not run: Gramps Web gave {result.Failure} to the name search. " +
                          "Search the tree for this person before adding more about them.");
        }
        else if (result.Partial)
        {
            sb.AppendLine();
            sb.AppendLine(PartialNote(result));
        }

        return sb.ToString();
    }

    /// <summary>A new person of create_records: where the call gave them, their name, new handle and check.</summary>
    internal sealed record BatchCheck(string Where, string Label, string Handle, CheckResult Result);

    /// <summary>
    /// The create_records reply when a strong match stops the call: per new person with possible duplicates, those
    /// and the other similar people, then how to go on.
    /// </summary>
    internal static string BatchRefusal(IReadOnlyList<BatchCheck> checks)
    {
        var refused = checks.Where(c => c.Result.Strong.Count > 0).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("type: Transaction");
        sb.AppendLine("action: refused");
        sb.AppendLine($"duplicates: {refused.Sum(c => c.Result.Strong.Count)}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Created none of the records: the tree may already have " +
                      $"{(refused.Count == 1 ? "one of the new people" : $"{refused.Count} of the new people")}.");
        foreach (var check in refused)
        {
            sb.AppendLine();
            sb.AppendLine($"{check.Where}, {BatchLabel(check)}:");
            sb.AppendLine($"  Possible duplicates ({check.Result.Strong.Count}):");
            AppendCandidates(sb, check.Result.Strong, "    ");
            if (check.Result.Weak.Count > 0)
            {
                sb.AppendLine($"  Similar people ({check.Result.Weak.Count}):");
                AppendCandidates(sb, check.Result.Weak, "    ");
            }

            if (check.Result.Partial)
                sb.AppendLine($"  {PartialNote(check.Result)}");
        }

        sb.AppendLine();
        sb.AppendLine("Next steps:");
        sb.AppendLine("  • Compare: get_object(objectType: \"person\", " +
                      $"identifier: \"{Identifier(refused[0].Result.Strong[0])}\", extended: true).");
        sb.AppendLine("  • The same person: leave the record out, give the existing person's handle where its \"#key\" " +
                      "was, and add the new details with update_person.");
        sb.AppendLine("  • A different person: call create_records again with the same records, adding notDuplicateOf " +
                      "to that record: " +
                      string.Join("; ", refused.Select(c => $"{c.Where} notDuplicateOf: {IdentifierList(c.Result.Strong)}")) + ".");
        return sb.ToString();
    }

    /// <summary>
    /// What create_records adds to its reply after saving: the similar people per new person with a merge hint, and
    /// notes on the people the check did not run for or did not read every match of; empty when there is nothing.
    /// </summary>
    internal static string BatchNotes(IReadOnlyList<BatchCheck> checks)
    {
        var sb = new StringBuilder();
        var similar = checks.Where(c => c.Result.Weak.Count > 0).ToList();
        if (similar.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Similar people already in the tree, possibly the same as a new person:");
            foreach (var check in similar)
            {
                sb.AppendLine($"  {check.Where}, {BatchLabel(check)} — handle: {check.Handle} ({check.Result.Weak.Count}):");
                AppendCandidates(sb, check.Result.Weak, "    ");
            }

            sb.AppendLine("If one of them is the new person, compare them with get_object and merge the new record into " +
                          "theirs: merge_objects(objectType: \"person\", keepHandle: \"<their handle>\", " +
                          "duplicateHandle: \"<the new person's handle>\").");
        }

        var failed = checks.Where(c => c.Result.Failure != null).ToList();
        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"The duplicate check did not run for {string.Join(", ", failed.Select(c => c.Where))}: " +
                          $"Gramps Web gave {failed[0].Result.Failure} to the name search. Search the tree for " +
                          $"{(failed.Count == 1 ? "this person" : "them")} before adding more about them.");
        }

        var partial = checks.Where(c => c.Result.Partial).ToList();
        if (partial.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("The duplicate check compared only part of the people with a matching name for " +
                          string.Join(", ", partial.Select(c => $"{c.Where} ({c.Result.Compared} of {c.Result.Matching})")) +
                          "; search the tree for the others.");
        }

        return sb.ToString();
    }

    private static string BatchLabel(BatchCheck check) =>
        string.IsNullOrWhiteSpace(check.Label) ? "(no name)" : check.Label.Trim();

    private static string PartialNote(CheckResult result) =>
        $"The duplicate check compared the first {result.Compared} of {result.Matching} people with a matching name; " +
        "search the tree for the others.";

    private static IReadOnlySet<string> Set(IEnumerable<string?>? values, StringComparer comparer) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToHashSet(comparer);

    [GeneratedRegex(@"\b\d{3,4}\b")]
    private static partial Regex YearRegex();
}
