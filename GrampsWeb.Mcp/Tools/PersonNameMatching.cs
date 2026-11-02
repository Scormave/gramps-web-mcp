using System.Globalization;
using System.Text;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The name comparison of the duplicate check. Names are folded so that spellings of one name compare equal
/// (ё and pre-reform letters, Latin accents, doubled letters, Мария and Марья, Дмитрий and Дмитрей), surnames
/// are cut to the stem the male and female forms share (Пугин and Пугина, Kowalski and Kowalska), and
/// patronymics to the father's name (Петрович, Петровна and the old Петров). The same rules give the Python
/// regular expressions Gramps Web's RegExpName filter rule takes, so the server returns the candidates.
/// </summary>
internal static class PersonNameMatching
{
    /// <summary>Endings of male and female patronymics, longest first.</summary>
    private static readonly string[] PatronymicEndings =
    [
        "инична", "ична", "овна", "евна", "ович", "евич", "ова", "ева", "ина", "ич", "ов", "ев", "ин",
        "ovich", "evich", "ovna", "evna", "ova", "eva", "ich", "ov", "ev"
    ];

    /// <summary>Folded Latin letters and the accented letters that fold to them.</summary>
    private static readonly Dictionary<char, string> LatinVariants = BuildLatinVariants();

    /// <summary>
    /// The form two spellings of one name share: lower case, ё and ѣ as е, і and ѵ as и, ѳ as ф, Latin letters
    /// without accents, no hard sign at the end, and doubled letters once. Given names also end in ья for ия and
    /// ей for ий.
    /// </summary>
    internal static string Fold(string? text, bool givenName = false)
    {
        var sb = new StringBuilder();
        foreach (var c in (text ?? "").Trim().ToLowerInvariant())
        {
            var folded = FoldChar(c);
            if (char.IsLetter(folded) && sb.Length > 0 && sb[^1] == folded)
                continue;
            sb.Append(folded);
        }

        if (sb.Length > 1 && sb[^1] == 'ъ')
            sb.Length--;
        var result = sb.ToString();
        if (givenName && result.Length > 2)
        {
            if (result.EndsWith("ия", StringComparison.Ordinal))
                result = result[..^2] + "ья";
            else if (result.EndsWith("ий", StringComparison.Ordinal))
                result = result[..^2] + "ей";
        }

        return result;
    }

    private static char FoldChar(char c) => c switch
    {
        'ё' or 'ѣ' => 'е',
        'і' or 'ѵ' => 'и',
        'ѳ' => 'ф',
        'ł' => 'l',
        'ø' => 'o',
        'đ' => 'd',
        >= 'à' and <= 'ſ' when c.ToString().Normalize(NormalizationForm.FormD) is { Length: > 1 } d
                                && char.IsAsciiLetterLower(d[0]) => d[0],
        _ => c
    };

    /// <summary>
    /// The stem of a surname, shared by its male and female forms: Пугин for Пугин and Пугина, Вознесенск for
    /// Вознесенский and Вознесенская, Толст for Толстой and Толстая, Kowalsk for Kowalski and Kowalska. Null when
    /// less than two letters are left.
    /// </summary>
    internal static string? SurnameStem(string? surname)
    {
        var folded = Fold(surname);
        var cut = folded switch
        {
            _ when EndsWithAny(folded, "ская", "цкая", "ское", "ский", "цкий", "ской", "цкой") => 2,
            _ when EndsWithAny(folded, "ska", "ski", "cka", "cki", "sky", "cky") => 1,
            _ when EndsWithAny(folded, "ова", "ева", "ина", "ына", "ova", "eva", "ina", "yna") => 1,
            { Length: > 4 } when EndsWithAny(folded, "ая", "ой", "ый", "ий") => 2,
            _ => 0
        };
        var stem = folded[..^cut];
        return stem.Count(char.IsLetter) >= 2 ? stem : null;
    }

    /// <summary>The father's name in a patronymic: Петр for Петрович, Петровна and Петров. Null when empty.</summary>
    internal static string? PatronymicRoot(string? patronymic)
    {
        var folded = Fold(patronymic);
        var ending = PatronymicEndings.FirstOrDefault(e => folded.Length - e.Length >= 2
                                                           && folded.EndsWith(e, StringComparison.Ordinal));
        var root = ending == null ? folded : folded[..^ending.Length];
        return root.Length == 0 ? null : root;
    }

    /// <summary>The folded words of a given name with at least two letters: Анна-Мария gives ана and марья.</summary>
    internal static IEnumerable<string> GivenTokens(string? firstName) =>
        Words(firstName).Select(w => Fold(w, givenName: true)).Where(w => w.Count(char.IsLetter) >= 2);

    private static IEnumerable<string> Words(string? text)
    {
        var word = new StringBuilder();
        foreach (var c in (text ?? "") + " ")
        {
            if (char.IsLetter(c))
            {
                word.Append(c);
            }
            else if (word.Length > 0)
            {
                yield return word.ToString();
                word.Clear();
            }
        }
    }

    /// <summary>
    /// A Python regular expression for RegExpName matching a word that begins with one of the stems, in any
    /// spelling that folds to it, with up to three more letters: Пугин, Пугина, Пугинъ. A word, not the whole
    /// field: RegExpName matches the surnames of a name joined into one text with their prefixes, so Пугинъ
    /// Ивановъ for a surname with a patronymic.
    /// </summary>
    internal static string SurnamePattern(IEnumerable<string> stems) =>
        $"(?<!\\w)(?:{string.Join("|", stems.Select(s => Pattern(s, givenName: false)))})\\w{{0,3}}(?!\\w)";

    /// <summary>A Python regular expression for RegExpName matching one of the folded given-name words.</summary>
    internal static string GivenPattern(IEnumerable<string> tokens) =>
        $"(?<!\\w)(?:{string.Join("|", tokens.Select(t => Pattern(t, givenName: true)))})ъ?(?!\\w)";

    /// <summary>
    /// The pattern of one folded text: each letter as the class of the letters that fold to it, repeated for
    /// doubled letters; given names end in [ьи]я for ья and [еи]й for ей.
    /// </summary>
    private static string Pattern(string folded, bool givenName)
    {
        var sb = new StringBuilder();
        var end = folded.Length;
        string? ending = null;
        if (givenName && folded.Length > 2 && folded.EndsWith("ья", StringComparison.Ordinal))
            (end, ending) = (folded.Length - 2, "[ьиіѵ]+я+");
        else if (givenName && folded.Length > 2 && folded.EndsWith("ей", StringComparison.Ordinal))
            (end, ending) = (folded.Length - 2, "[еёѣиіѵ]+й+");

        foreach (var c in folded[..end])
        {
            if (!char.IsLetter(c))
            {
                sb.Append(char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c) ? $"\\{c}" : c.ToString());
                continue;
            }

            var variants = c switch
            {
                'е' => "еёѣ",
                'и' => "иіѵ",
                'ф' => "фѳ",
                _ => LatinVariants.GetValueOrDefault(c, c.ToString())
            };
            sb.Append(variants.Length == 1 ? $"{c}+" : $"[{variants}]+");
        }

        return sb.Append(ending).ToString();
    }

    private static Dictionary<char, string> BuildLatinVariants()
    {
        var variants = new Dictionary<char, StringBuilder>();
        for (var c = 'à'; c <= 'ſ'; c++)
        {
            var folded = FoldChar(c);
            if (folded != c && char.IsAsciiLetterLower(folded))
                (variants.TryGetValue(folded, out var sb) ? sb : variants[folded] = new StringBuilder(folded.ToString()))
                    .Append(c);
        }

        return variants.ToDictionary(v => v.Key, v => v.Value.ToString());
    }

    private static bool EndsWithAny(string text, params string[] endings) =>
        endings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    /// <summary>Whether a surname's origin is a patronymic or matronymic, by name or Gramps' number.</summary>
    private static bool IsParentName(string? originType, bool patronymicOnly = false) =>
        originType?.Trim() is { } origin
        && (origin.Equals("Patronymic", StringComparison.OrdinalIgnoreCase) || origin == "5"
            || !patronymicOnly && (origin.Equals("Matronymic", StringComparison.OrdinalIgnoreCase) || origin == "6"));

    /// <summary>The parts of a person's names the duplicate check compares, over all names.</summary>
    internal sealed record NameKeys(
        IReadOnlySet<string> Given, IReadOnlySet<string> SurnameStems, IReadOnlySet<string> PatronymicRoots)
    {
        /// <summary>The same given name and the same surname, in any of the names.</summary>
        public bool SameName(NameKeys other) => Given.Overlaps(other.Given) && SurnameStems.Overlaps(other.SurnameStems);

        /// <summary>Both have patronymics, and no father's name is the same.</summary>
        public bool OtherPatronymic(NameKeys other) =>
            PatronymicRoots.Count > 0 && other.PatronymicRoots.Count > 0 && !PatronymicRoots.Overlaps(other.PatronymicRoots);
    }

    internal static NameKeys Keys(GrampsNameRequest? primary, IEnumerable<GrampsNameRequest>? alternates) =>
        Keys(new[] { primary }.Concat(alternates ?? []).OfType<GrampsNameRequest>()
            .Select(n => (n.FirstName, (n.SurnameList ?? []).Select(s => (s.Surname, s.OriginType)))));

    internal static NameKeys Keys(GrampsName? primary, IEnumerable<GrampsName>? alternates) =>
        Keys(new[] { primary }.Concat(alternates ?? []).OfType<GrampsName>()
            .Select(n => (n.FirstName, (n.SurnameList ?? []).Select(s => (s.Surname, s.OriginType)))));

    private static NameKeys Keys(
        IEnumerable<(string? FirstName, IEnumerable<(string? Surname, string? OriginType)> Surnames)> names)
    {
        var given = new HashSet<string>(StringComparer.Ordinal);
        var stems = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (firstName, surnames) in names)
        {
            given.UnionWith(GivenTokens(firstName));
            foreach (var (surname, originType) in surnames)
            {
                if (string.IsNullOrWhiteSpace(surname))
                    continue;
                if (IsParentName(originType, patronymicOnly: true))
                {
                    if (PatronymicRoot(surname) is { } root)
                        roots.Add(root);
                }
                else if (!IsParentName(originType) && SurnameStem(surname) is { } stem)
                {
                    stems.Add(stem);
                }
            }
        }

        return new NameKeys(given, stems, roots);
    }
}
