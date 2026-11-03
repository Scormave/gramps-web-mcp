using System.Globalization;
using System.Text;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Requests;

namespace GrampsWeb.Mcp.Tools;

/// <summary>
/// The name comparison of the duplicate check. Names are folded to keys in Cyrillic letters, so that spellings of
/// one name in either alphabet compare equal (Иванов, Ivanov and Iwanow; ё and pre-reform letters, Latin accents,
/// doubled letters, Мария, Марья and Maria). A surname gives several keys, the folded surname and the stems its male
/// and female forms share (Образцов and Образцова, Kowalski and Kowalska, Novák and Nováková, Kazlauskas and
/// Kazlauskienė), and two surnames match when they share one; patronymics are cut to the father's name (Петрович,
/// Петровна and the old Петров). The same keys give the Python regular expressions Gramps Web's RegExpName filter
/// rule takes, so the server returns the candidates.
/// </summary>
internal static class PersonNameMatching
{
    /// <summary>Latin letter groups read as one Cyrillic letter or two, longest first.</summary>
    private static readonly (string Latin, string Key)[] LatinGroups =
    [
        ("tsch", "ч"), ("szcz", "шч"), ("shch", "шч"), ("sch", "ш"), ("sz", "ш"), ("sh", "ш"), ("cz", "ч"),
        ("ch", "ч"), ("zh", "ж"), ("rz", "ж"), ("kh", "г"), ("ts", "ц"), ("tz", "ц"), ("ph", "ф"), ("ou", "у")
    ];

    /// <summary>The key letters of a to z.</summary>
    private static readonly string[] LatinKeys =
    [
        "а", "б", "ц", "д", "е", "ф", "г", "г", "и", "и", "к", "л", "м", "н", "о", "п", "к", "р", "с", "т", "у", "в",
        "в", "кс", "и", "з"
    ];

    /// <summary>
    /// The letters each key letter stands for besides accented Latin ones, and the letter groups that fold to it.
    /// </summary>
    private static readonly Dictionary<char, (string Letters, string[] Groups)> KeyLetters = new()
    {
        ['а'] = ("аяa", []),
        ['б'] = ("бb", []),
        ['в'] = ("вvw", []),
        ['г'] = ("гґхgh", ["kh", "ch"]),
        ['д'] = ("дd", []),
        ['е'] = ("еёэєѣe", []),
        ['ж'] = ("ж", ["zh", "rz"]),
        ['з'] = ("зz", []),
        ['и'] = ("иыйіїѵiyj", []),
        ['к'] = ("кkq", []),
        ['л'] = ("лl", []),
        ['м'] = ("мm", []),
        ['н'] = ("нn", []),
        ['о'] = ("оo", []),
        ['п'] = ("пp", []),
        ['р'] = ("рr", []),
        ['с'] = ("сsß", []),
        ['т'] = ("тt", []),
        ['у'] = ("уюўu", ["ou"]),
        ['ф'] = ("фѳf", ["ph"]),
        ['ц'] = ("цc", ["тс", "ts", "tz"]),
        ['ч'] = ("ч", ["cz", "ch", "tsch"]),
        ['ш'] = ("ш", ["sz", "sh", "sch"])
    };

    /// <summary>
    /// What each key letter stands for in a Python regular expression: its letters, the accented Latin letters
    /// that fold to it, and its letter groups. No accented letter is in two classes, so a run of them has one way
    /// to match and the server's regular expression engine does not try every split of it between letters.
    /// </summary>
    private static readonly Dictionary<char, string> KeyPatterns = KeyLetters.ToDictionary(k => k.Key, k =>
    {
        var letters = $"[{k.Value.Letters}{AccentedLetters(k.Key)}]";
        return k.Value.Groups.Length == 0 ? letters : $"(?:{letters}|{string.Join("|", k.Value.Groups)})";
    });

    /// <summary>The letters that fold to и, which the key drops before а, е and у: the и of ия, the y of ya, the j of ja.</summary>
    private static readonly string Iotation = KeyPatterns['и'] + "*";

    /// <summary>Endings of male and female patronymics as keys, longest first.</summary>
    private static readonly string[] PatronymicEndings =
        ["инична", "ична", "ивна", "овна", "евна", "ович", "евич", "ова", "ева", "ина", "ич", "ов", "ев", "ин"];

    /// <summary>
    /// The key two spellings of one name share, in Cyrillic letters: lower case; Latin letters and letter groups as
    /// the Cyrillic letters they are read as (w as в, sz and sh as ш, cz and ch as ч, ts as ц, h and kh as г, y and j
    /// as и), without accents; ё, э and ѣ as е, ы, й and і as и, х as г, тс as ц; no soft or hard signs; я, ю and
    /// the и or й before а, е and у as а, е and у, and before о as е, as for ё (Piotr and Петр); doubled letters
    /// once. Given names end in и for ей (Андрей and Andrei).
    /// </summary>
    internal static string Fold(string? text, bool givenName = false) => Fold(text, givenName, chAsKh: false);

    /// <summary>The keys of a text: a Latin ch is read as ч, and also as х (Polish, Czech and German).</summary>
    internal static IReadOnlySet<string> Folds(string? text, bool givenName = false)
    {
        var chAsKh = (text ?? "").Contains("ch", StringComparison.OrdinalIgnoreCase);
        return new HashSet<string>([Fold(text, givenName, false), Fold(text, givenName, chAsKh)], StringComparer.Ordinal);
    }

    private static string Fold(string? text, bool givenName, bool chAsKh)
    {
        var lower = (text ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        for (var i = 0; i < lower.Length;)
        {
            var (latin, key) = LatinGroups.FirstOrDefault(g => lower.AsSpan(i).StartsWith(g.Latin));
            if (latin != null)
            {
                sb.Append(latin == "ch" && chAsKh ? "г" : key);
                i += latin.Length;
            }
            else
            {
                sb.Append(KeyOf(lower[i++]));
            }
        }

        var result = Simplify(sb.Replace("тс", "ц").ToString());
        return givenName && result.Length > 2 && result.EndsWith("еи", StringComparison.Ordinal)
            ? result[..^2] + "и"
            : result;
    }

    /// <summary>
    /// The accented Latin letters à to ſ whose key is the key letter, for a character class: runs of three or more
    /// as ranges, which may take in upper-case letters (Ā-ą), as RegExpName ignores case.
    /// </summary>
    private static string AccentedLetters(char key)
    {
        var letters = Enumerable.Range('à', 'ſ' - 'à' + 1).Select(c => (char)c)
            .Where(c => char.IsLower(c) && KeyOf(c) == key.ToString()).ToList();
        var codes = letters.Concat(letters.Select(char.ToUpperInvariant).Where(c => c is >= 'Ā' and <= 'ſ'))
            .Distinct().Order().ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < codes.Count;)
        {
            var j = i;
            while (j + 1 < codes.Count && codes[j + 1] == codes[j] + 1)
                j++;
            if (j - i >= 2)
                sb.Append(codes[i]).Append('-').Append(codes[j]);
            else
                sb.Append(string.Concat(codes.Skip(i).Take(j - i + 1).Where(letters.Contains)));
            i = j + 1;
        }

        return sb.ToString();
    }

    private static string KeyOf(char c) => c switch
    {
        'ё' or 'э' or 'є' or 'ѣ' => "е",
        'ы' or 'й' or 'і' or 'ї' or 'ѵ' => "и",
        'я' => "иа",
        'ю' => "иу",
        'щ' => "шч",
        'ь' or 'ъ' => "",
        'ґ' or 'х' => "г",
        'ў' => "у",
        'ѳ' => "ф",
        'š' => "ш",
        'č' or 'ć' => "ч",
        'ž' or 'ż' => "ж",
        'ß' => "с",
        'ł' => "л",
        'ø' => "о",
        'đ' => "д",
        >= 'à' and <= 'ſ' when c.ToString().Normalize(NormalizationForm.FormD) is { Length: > 1 } d
                                && char.IsAsciiLetterLower(d[0]) => LatinKeys[d[0] - 'a'],
        >= 'a' and <= 'z' => LatinKeys[c - 'a'],
        _ => c.ToString()
    };

    /// <summary>
    /// Doubled letters once; и before а, е and у dropped, and ио as е; о after ж, ч and ш as е, as ё after them is
    /// written o in Latin letters (Gorbaczow).
    /// </summary>
    private static string Simplify(string key)
    {
        var sb = new StringBuilder();
        foreach (var letter in key)
        {
            var c = letter;
            if (c is 'а' or 'е' or 'у' or 'о' && sb.Length > 0 && sb[^1] == 'и')
            {
                sb.Length--;
                if (c == 'о')
                    c = 'е';
            }

            if (c == 'о' && sb.Length > 0 && sb[^1] is 'ж' or 'ч' or 'ш')
                c = 'е';

            if (!char.IsLetter(c) || sb.Length == 0 || sb[^1] != c)
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The keys a surname shares with its other forms: the folded surname, and the stems left when endings of male,
    /// female and daughters' forms are cut, keeping at least three letters. Cut in any alphabet: а, и and ои
    /// (Образцова, Kowalski and Kowalska, Толстой and Толстая), and ое after ск or цк. Cut in Latin letters
    /// only: ova and owa (Novák and Nováková, Nowak and Nowakowa), ówna (Nowakówna), ienė, aitė, ytė and utė
    /// (Kazlauskas, Kazlauskienė and Kazlauskaitė), as, is, us and os, s and š after a consonant (Ozols and Ozola),
    /// e, ou (Papadopoulos and Papadopoulou) and né (Kovács and Kovácsné); and in Latin letters, a final ek, ec or
    /// el with the e dropped, as the female forms drop it (Hájek and Hájková, Němec and Němcová). Empty when less
    /// than two letters are left.
    /// </summary>
    internal static IReadOnlySet<string> SurnameStems(string? surname)
    {
        var latin = (surname ?? "").Any(c => char.IsAsciiLetter(c) || c is >= 'À' and <= 'ſ' && char.IsLetter(c));
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Folds(surname))
        {
            if (key.Count(char.IsLetter) < 2)
                continue;
            stems.Add(key);
            foreach (var cut in Cuts(key, latin))
            {
                if (key.Length - cut >= 3)
                    stems.Add(key[..^cut]);
            }

            if (latin && key.Length >= 4 && key[^2] == 'е' && key[^1] is 'к' or 'ц' or 'л')
                stems.Add(key[..^2] + (key[^3] is 'а' or 'о' or 'у' ? "и" : "") + key[^1]);
        }

        return stems;
    }

    private static IEnumerable<int> Cuts(string key, bool latin)
    {
        if (key.EndsWith('а') || key.EndsWith('и'))
            yield return 1;
        if (EndsWithAny(key, "ои", "ское", "цкое"))
            yield return 2;
        if (!latin)
            yield break;

        if (EndsWithAny(key, "ова", "ева", "ене", "ите", "уте"))
            yield return 3;
        if (EndsWithAny(key, "овна", "евна", "аите"))
            yield return 4;
        if (EndsWithAny(key, "ас", "ис", "ус", "ос", "не"))
            yield return 2;
        if (key.Length > 1 && key[^1] is 'с' or 'ш' && key[^2] is not ('а' or 'е' or 'и' or 'о' or 'у')
            || key.EndsWith('е') || key.EndsWith('у'))
            yield return 1;
    }

    /// <summary>
    /// The fathers' names in a patronymic: Петр for Петрович, Петровна, the old Петров, Petrovich and Pietrowicz.
    /// Empty when empty.
    /// </summary>
    internal static IReadOnlySet<string> PatronymicRoots(string? patronymic)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in Folds(patronymic))
        {
            var ending = PatronymicEndings.FirstOrDefault(e => key.Length - e.Length >= 2
                                                               && key.EndsWith(e, StringComparison.Ordinal));
            var root = ending == null ? key : key[..^ending.Length];
            if (root.Length > 0)
                roots.Add(root);
        }

        return roots;
    }

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
    /// spelling that folds to it, with up to eight more letters, the endings two forms of one surname can differ by:
    /// Образцов, Образцова, Obraztsova, Образцовъ. A word, not the whole field: RegExpName matches the surnames of a
    /// name joined into one text with their prefixes, so Образцовъ Ивановъ for a surname with a patronymic.
    /// </summary>
    internal static string SurnamePattern(IEnumerable<string> stems) =>
        $"(?<!\\w)(?:{string.Join("|", stems.Select(s => Pattern(s, givenName: false)))})\\w{{0,8}}(?!\\w)";

    /// <summary>A Python regular expression for RegExpName matching one of the given-name keys as a word.</summary>
    internal static string GivenPattern(IEnumerable<string> keys) =>
        $"(?<!\\w)(?:{string.Join("|", keys.Select(k => Pattern(k, givenName: true)))})(?!\\w)";

    /// <summary>
    /// The pattern of one key: each letter as what it stands for, repeated for doubled letters; а, е and у after
    /// any letter that folds to и, е also as ио and after ж, ч and ш as о, and other letters with an optional soft
    /// or hard sign after them. кс also matches x with the k before it and the s after it that fold into the same
    /// two letters (Kx, xs), and шч also matches щ. A given name's final и also matches ей; a surname stem's final
    /// к also matches x, as cutting an ending can split the кс of an x, and its final к, ц or л after a consonant
    /// also matches with е before it, the е its female forms drop (Hájek for Hájková).
    /// </summary>
    private static string Pattern(string key, bool givenName)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            var next = i + 1 < key.Length ? key[i + 1] : '\0';
            var last = !givenName && i == key.Length - 1;
            var letter = last && key[i] == 'к' ? $"(?:{Letter('к')}|x)" : Letter(key[i]);
            if (key[i] == 'к' && next == 'с')
            {
                sb.Append($"(?:{KeyPatterns['к']}*x+{KeyPatterns['с']}*[ьъ]?|{Letter('к')}{Letter('с')})");
                i++;
            }
            else if (key[i] == 'ш' && next == 'ч')
            {
                sb.Append($"(?:щ+|{Letter('ш')}{Letter('ч')})");
                i++;
            }
            else if (givenName && key[i] == 'и' && i == key.Length - 1 && i > 1)
            {
                sb.Append($"(?:{Iotation}{KeyPatterns['е']})?{Letter('и')}");
            }
            else if (key[i] == 'е' && i > 0 && key[i - 1] is 'ж' or 'ч' or 'ш')
            {
                sb.Append($"(?:{Iotation}(?:{KeyPatterns['е']}|{KeyPatterns['о']}))+");
            }
            else if (last && i > 1 && key[i] is 'к' or 'ц' or 'л' && key[i - 1] is not ('а' or 'е' or 'о' or 'у'))
            {
                sb.Append($"(?:{Letter('е')})?{letter}");
            }
            else
            {
                sb.Append(letter);
            }
        }

        return sb.ToString();
    }

    private static string Letter(char c) => c switch
    {
        'а' or 'у' => $"(?:{Iotation}{KeyPatterns[c]})+",
        'е' => $"(?:{Iotation}{KeyPatterns['е']}|{Iotation[..^1]}+{KeyPatterns['о']})+",
        'и' => $"{KeyPatterns[c]}+",
        _ when KeyPatterns.TryGetValue(c, out var letters) => $"{letters}+[ьъ]?",
        _ when char.IsAscii(c) && !char.IsAsciiLetterOrDigit(c) => $"\\{c}",
        _ when char.IsLetter(c) => $"{c}+",
        _ => c.ToString()
    };

    private static bool EndsWithAny(string text, params string[] endings) =>
        endings.Any(e => text.EndsWith(e, StringComparison.Ordinal));

    /// <summary>Whether a surname's origin is a patronymic or matronymic, by name or Gramps' number.</summary>
    private static bool IsParentName(string? originType, bool patronymicOnly = false) =>
        originType?.Trim() is { } origin
        && (origin.Equals("Patronymic", StringComparison.OrdinalIgnoreCase) || origin == "5"
            || !patronymicOnly && (origin.Equals("Matronymic", StringComparison.OrdinalIgnoreCase) || origin == "6"));

    /// <summary>
    /// The keys of a person's names the duplicate check compares, over all names; <paramref name="FirstGiven"/>
    /// holds the keys of the first word of each given name.
    /// </summary>
    internal sealed record NameKeys(
        IReadOnlySet<string> Given, IReadOnlySet<string> FirstGiven, IReadOnlySet<string> SurnameStems,
        IReadOnlySet<string> PatronymicRoots)
    {
        /// <summary>
        /// The first word of one's given name is a word of the other's: Мария and Анна Мария, but not Robert de and
        /// Gilbert de, or Иван Петрович and Петр Петрович.
        /// </summary>
        public bool SameGivenName(NameKeys other) => FirstGiven.Overlaps(other.Given) || other.FirstGiven.Overlaps(Given);

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
        var first = new HashSet<string>(StringComparer.Ordinal);
        var stems = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (firstName, surnames) in names)
        {
            var words = Words(firstName)
                .Select(w => Folds(w, givenName: true).Where(k => k.Count(char.IsLetter) >= 2).ToList())
                .Where(keys => keys.Count > 0)
                .ToList();
            foreach (var keys in words)
                given.UnionWith(keys);
            if (words.Count > 0)
                first.UnionWith(words[0]);
            foreach (var (surname, originType) in surnames)
            {
                if (string.IsNullOrWhiteSpace(surname))
                    continue;
                if (IsParentName(originType, patronymicOnly: true))
                    roots.UnionWith(PatronymicRoots(surname));
                else if (!IsParentName(originType))
                    stems.UnionWith(SurnameStems(surname));
            }
        }

        return new NameKeys(given, first, stems, roots);
    }
}
