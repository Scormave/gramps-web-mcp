using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Input;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>
/// The duplicate check of create_records: name folding, the server search, which namesakes are strong or weak
/// matches, and the replies.
/// </summary>
public class DuplicatePeopleTests
{
    [Theory]
    [InlineData("Образцовъ", false, "образцов")]
    [InlineData("Obraztsov", false, "образцов")]
    [InlineData("Obrazcow", false, "образцов")]
    [InlineData("Алёна", false, "алена")]
    [InlineData("Ѳеодоръ", false, "феодор")]
    [InlineData("Аввакум", false, "авакум")]
    [InlineData("Łukasz", false, "лукаш")]
    [InlineData("Müller", false, "мулер")]
    [InlineData("Мария", true, "мара")]
    [InlineData("Марья", true, "мара")]
    [InlineData("Marija", true, "мара")]
    [InlineData("Дмитрий", true, "дмитри")]
    [InlineData("Дмитрей", true, "дмитри")]
    [InlineData("Dmitriy", true, "дмитри")]
    [InlineData("Andrei", true, "андри")]
    [InlineData("Piotr", true, "петр")]
    [InlineData("Іоаннъ", true, "еан")]
    public void Fold_Gives_Spellings_Of_One_Name_One_Form(string text, bool givenName, string expected) =>
        Assert.Equal(expected, PersonNameMatching.Fold(text, givenName));

    [Theory]
    [InlineData("Агнешка", "Agnieszka")]
    [InlineData("Войцех", "Wojciech")]
    [InlineData("Гжегож", "Grzegorz")]
    [InlineData("Чеслав", "Czesław")]
    [InlineData("Ядвига", "Jadwiga")]
    [InlineData("Ксения", "Xenia")]
    [InlineData("Евгений", "Jewgienij")]
    [InlineData("Евгений", "Yevgeniy")]
    [InlineData("Алексей", "Aleksiej")]
    [InlineData("Алексей", "Alexei")]
    [InlineData("Майя", "Maja")]
    [InlineData("Мая", "Maja")]
    public void Folds_Of_A_Given_Name_In_Cyrillic_And_Latin_Letters_Overlap(string cyrillic, string latin) =>
        Assert.True(PersonNameMatching.Folds(cyrillic, givenName: true)
            .Overlaps(PersonNameMatching.Folds(latin, givenName: true)));

    [Theory]
    [InlineData("Горбачёв", "Gorbaczow")]
    [InlineData("Горбачёв", "Gorbachev")]
    [InlineData("Горбачёв", "Gorbatschow")]
    [InlineData("Заяц", "Zając")]
    public void Folds_Of_A_Surname_In_Cyrillic_And_Latin_Letters_Overlap(string cyrillic, string latin) =>
        Assert.True(PersonNameMatching.Folds(cyrillic, givenName: false)
            .Overlaps(PersonNameMatching.Folds(latin, givenName: false)));

    [Theory]
    [InlineData("Образцов", "Образцова", true)]
    [InlineData("Образцов", "Obraztsova", true)]
    [InlineData("Образцова", "Obrazcow", true)]
    [InlineData("Образцовъ", "OBRAZTSOV", true)]
    [InlineData("Дубровский", "Дубровская", true)]
    [InlineData("Дубровский", "Dubrowska", true)]
    [InlineData("Дубровская", "Dubrovsky", true)]
    [InlineData("Дубровской", "Dubrovskaya", true)]
    [InlineData("Kowalski", "Kowalska", true)]
    [InlineData("Novák", "Nováková", true)]
    [InlineData("Novák", "Новак", true)]
    [InlineData("Nowak", "Nowakowa", true)]
    [InlineData("Nowak", "Nowakówna", true)]
    [InlineData("Svoboda", "Svobodová", true)]
    [InlineData("Свобода", "Svobodová", true)]
    [InlineData("Kazlauskas", "Kazlauskienė", true)]
    [InlineData("Kazlauskas", "Kazlauskaitė", true)]
    [InlineData("Kazlauskienė", "Kazlauskaitė", true)]
    [InlineData("Ozols", "Ozola", true)]
    [InlineData("Papadopoulos", "Papadopoulou", true)]
    [InlineData("Kovács", "Kovácsné", true)]
    [InlineData("Hájek", "Hájková", true)]
    [InlineData("Němec", "Němcová", true)]
    [InlineData("Образцов", "Образцовский", false)]
    [InlineData("Образцов", "Собразцов", false)]
    [InlineData("Kowalski", "Kowalczyk", false)]
    [InlineData("Ozols", "Ozoliņš", false)]
    [InlineData("Novák", "Novotný", false)]
    public void SurnameStems_Of_Forms_Of_One_Surname_Overlap(string surname, string other, bool overlap) =>
        Assert.Equal(overlap, PersonNameMatching.SurnameStems(surname).Overlaps(PersonNameMatching.SurnameStems(other)));

    [Theory]
    [InlineData("Образцова", "образцова,образцов")]
    [InlineData("Nováková", "новакова,новаков,новак")]
    [InlineData("Kovács", "ковацс,ковац")]
    [InlineData("Я", "")]
    [InlineData("", "")]
    public void SurnameStems_Are_The_Surname_And_Its_Stems(string surname, string expected) =>
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(),
            PersonNameMatching.SurnameStems(surname).Order());

    [Theory]
    [InlineData("Петрович", "петр")]
    [InlineData("Петровна", "петр")]
    [InlineData("Петров", "петр")]
    [InlineData("Петрівна", "петр")]
    [InlineData("Petrovich", "петр")]
    [InlineData("Pietrowicz", "петр")]
    [InlineData("Ильич", "ил")]
    [InlineData("Ильинична", "ил")]
    [InlineData("Юрьевич", "ур")]
    [InlineData("Николаевич", "никола")]
    [InlineData("Nikolajewicz", "никола")]
    public void PatronymicRoots_Hold_The_Fathers_Name(string patronymic, string expected) =>
        Assert.Contains(expected, PersonNameMatching.PatronymicRoots(patronymic));

    [Fact]
    public void PatronymicRoots_Of_Nothing_Are_Empty() =>
        Assert.Empty(PersonNameMatching.PatronymicRoots(" "));

    [Theory]
    [InlineData("Образцов", true)]
    [InlineData("ОБРАЗЦОВА", true)]
    [InlineData("Образцовъ", true)]
    [InlineData("Обрразцова", true)]
    [InlineData("Образцовъ Ивановъ", true)]
    [InlineData("de Образцов", true)]
    [InlineData("Obraztsov", true)]
    [InlineData("Obrazcowa", true)]
    [InlineData("Образцовский", true)]
    [InlineData("Собразцов", false)]
    [InlineData("Obrazek", false)]
    public void SurnamePattern_Matches_The_Stem_In_Any_Spelling_With_An_Ending(string surname, bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.SurnamePattern(["образцов"]), surname));

    [Theory]
    [InlineData("Kazlauskas", true)]
    [InlineData("Kazlauskienė", true)]
    [InlineData("Kazlauskaitė", true)]
    [InlineData("Казлаускас", true)]
    [InlineData("Kazlauskis", true)]
    [InlineData("Kazlowski", false)]
    public void SurnamePattern_Of_A_Stem_Matches_The_Male_And_Female_Forms(string surname, bool matches) =>
        Assert.Equal(matches,
            Matches(PersonNameMatching.SurnamePattern(PersonNameMatching.SurnameStems("Kazlauskienė")), surname));

    [Theory]
    [InlineData("Hájková", "Hájek", true)]
    [InlineData("Hájek", "Hájková", true)]
    [InlineData("Němcová", "Němec", true)]
    [InlineData("Němcová", "Немец", true)]
    [InlineData("Němec", "Němcová", true)]
    [InlineData("Němcová", "Němeček", false)]
    public void SurnamePattern_Finds_The_Male_Form_With_The_E_The_Female_Form_Drops(string surname, string other,
        bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.SurnamePattern(PersonNameMatching.SurnameStems(surname)), other));

    [Theory]
    [InlineData("Горбачёв", "Gorbaczow")]
    [InlineData("Горбачёв", "Gorbachev")]
    [InlineData("Горбачёв", "Gorbatschow")]
    [InlineData("Gorbaczow", "Горбачева")]
    [InlineData("Zając", "Заяц")]
    [InlineData("Zając", "Zajac")]
    [InlineData("Заяц", "Zając")]
    public void SurnamePattern_Matches_The_Surname_In_The_Other_Alphabet(string surname, string other) =>
        Assert.True(Matches(PersonNameMatching.SurnamePattern(PersonNameMatching.SurnameStems(surname)), other));


    /// <summary>
    /// The server runs the pattern on every name in the tree, so a long run of letters it can match more than one
    /// way must not make it try every split of the run before it fails.
    /// </summary>
    [Theory]
    [InlineData("Kazlauskienė", "àéìòùąęįų")]
    [InlineData("Hájková", "áéíóúýěů")]
    [InlineData("Zając", "ąęóàáâãä")]
    [InlineData("Gorbaczow", "óòôõöøeo")]
    [InlineData("Образцова", "аяеёоиыйij")]
    [InlineData("Kxnhxbkard", "xkqsx")]
    public void SurnamePattern_Fails_Fast_On_A_Long_Run_Of_Letters_It_Could_Split_Many_Ways(string surname, string letters)
    {
        var pattern = new Regex(PersonNameMatching.SurnamePattern(PersonNameMatching.SurnameStems(surname)),
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var run = string.Concat(Enumerable.Repeat(letters, 40 / letters.Length + 1));

        foreach (var text in new[] { surname + run, surname[..3] + run + surname[3..] + run, run + "1" })
            pattern.IsMatch(text);
    }

    [Theory]
    [InlineData("Мария", true)]
    [InlineData("Марья", true)]
    [InlineData("Марія", true)]
    [InlineData("Maria", true)]
    [InlineData("Marija", true)]
    [InlineData("María", true)]
    [InlineData("Анна Мария", true)]
    [InlineData("Марьяна", false)]
    [InlineData("Mariana", false)]
    [InlineData("Анна", false)]
    public void GivenPattern_Matches_A_Word_Of_The_Given_Name_In_Any_Spelling(string firstName, bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.GivenPattern(["мара"]), firstName));

    [Theory]
    [InlineData("Андрей", true)]
    [InlineData("Андрий", true)]
    [InlineData("Andrei", true)]
    [InlineData("Andrey", true)]
    [InlineData("Andrej", true)]
    [InlineData("Андриан", false)]
    public void GivenPattern_Matches_Ей_And_Ий(string firstName, bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.GivenPattern(["андри"]), firstName));

    [Theory]
    [InlineData("Maja", true)]
    [InlineData("Maya", true)]
    [InlineData("Майя", true)]
    [InlineData("Мая", true)]
    [InlineData("Мария", false)]
    public void GivenPattern_Matches_A_J_Between_Vowels_In_Any_Spelling(string firstName, bool matches) =>
        Assert.Equal(matches,
            Matches(PersonNameMatching.GivenPattern(PersonNameMatching.Folds("Maja", givenName: true)), firstName));

    [Fact]
    public void SearchPath_Selects_By_Surname_And_Given_Name_In_One_Request()
    {
        var path = DuplicatePeople.SearchPath(Keys("""{ "given": "Иван", "surname": "Образцов", "patronymic": "Петрович" }"""));

        Assert.NotNull(path);
        Assert.StartsWith("/api/people/?rules=", path);
        Assert.EndsWith("&keys=handle,gramps_id,gender,primary_name,alternate_names,parent_family_list,profile" +
                        "&profile=families&page=1&pagesize=100", path);
        var rules = Rules(path);
        Assert.Equal("and", rules["function"]!.GetValue<string>());
        Assert.Equal(["RegExpName", "RegExpName"], rules["rules"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()));
        var patterns = rules["rules"]!.AsArray().Select(r => r!["values"]![0]!.GetValue<string>()).ToList();
        Assert.True(Matches(patterns[0], "Образцова"));
        Assert.True(Matches(patterns[0], "Obraztsova"));
        Assert.True(Matches(patterns[1], "Анна Иван"));
        Assert.True(Matches(patterns[1], "Iwan"));
    }

    [Fact]
    public void SearchPath_Sends_Only_The_Shortest_Of_Stems_That_Begin_With_Another()
    {
        var path = DuplicatePeople.SearchPath(Keys("""{ "given": "Agnieszka", "surname": "Nowakówna" }"""));

        Assert.NotNull(path);
        var pattern = Rules(path)["rules"]![0]!["values"]![0]!.GetValue<string>();
        Assert.Equal(PersonNameMatching.SurnamePattern(["новак"]), pattern);
        Assert.All(new[] { "Nowak", "Nowakowa", "Nowakówna", "Novák", "Nováková", "Новак" },
            surname => Assert.True(Matches(pattern, surname), surname));
    }

    [Fact]
    public void SearchPath_Sends_Only_The_Stem_Whose_Pattern_Matches_The_Others()
    {
        var path = DuplicatePeople.SearchPath(Keys("""{ "given": "Agnieszka", "surname": "Hájková" }"""));

        Assert.NotNull(path);
        var pattern = Rules(path)["rules"]![0]!["values"]![0]!.GetValue<string>();
        Assert.Equal(PersonNameMatching.SurnamePattern(["гаик"]), pattern);
        Assert.All(new[] { "Hájek", "Hájková" },
            surname => Assert.True(Matches(pattern, surname), surname));
    }

    /// <summary>
    /// An x folds to кс, which joins a k before it or an s after it, and cutting an ending can split it; the fleeting
    /// е of a stem can also be spelled io or ie. The search must still find the surname itself and its other form.
    /// </summary>
    [Theory]
    [InlineData("Qndiehnthx")]
    [InlineData("Aaaqqkxbyr")]
    [InlineData("Kxnhxbkard")]
    [InlineData("Hcoxscbhey")]
    [InlineData("Gcanmdpfxx")]
    [InlineData("Iqqtoarqex")]
    [InlineData("Zrtossmyoc")]
    [InlineData("Qsuhnbpioq")]
    [InlineData("Zedahxeyec")]
    public void SearchPath_Finds_The_Surname_It_Searches_For(string surname)
    {
        foreach (var (form, other) in new[] { (surname, surname), (surname + "ova", surname + "ov"),
                     (surname + "ov", surname + "ova") })
        {
            var path = DuplicatePeople.SearchPath(Keys($$"""{ "given": "Anna", "surname": "{{form}}" }"""));

            Assert.NotNull(path);
            Assert.True(Matches(Rules(path)["rules"]![0]!["values"]![0]!.GetValue<string>(), other), $"{form} -> {other}");
        }
    }

    [Fact]
    public void SearchPath_Drops_The_Given_Name_When_The_Rules_Get_Too_Long()
    {
        var given = "абвг".Select(c => new string('е', 30) + c).ToHashSet();
        var names = new PersonNameMatching.NameKeys(given, given, new HashSet<string> { "образцов" }, new HashSet<string>());

        var path = DuplicatePeople.SearchPath(names);

        Assert.NotNull(path);
        var rules = Rules(path)["rules"]!.AsArray();
        Assert.Single(rules);
        Assert.True(Matches(rules[0]!["values"]![0]!.GetValue<string>(), "Образцов"));
    }

    [Theory]
    [InlineData("""{ "given": "Иван" }""")]
    [InlineData("""{ "surname": "Образцов" }""")]
    public void SearchPath_Needs_A_Surname_And_A_Given_Name(string name) =>
        Assert.Null(DuplicatePeople.SearchPath(Keys(name)));

    [Fact]
    public void Classify_Sorts_Namesakes_Into_Strong_And_Weak_And_Drops_The_Others()
    {
        var person = NewPerson("""{ "given": "Иван", "surname": "Образцов", "patronymic": "Петрович" }""", gender: 1,
            birthYear: 1880);
        var people = People(
            Person("h-same-year", "I0001", "Іван", "Образцовъ", 1, birth: "1880"),
            Person("h-two-years", "I0002", "Иван", "Образцов", 1, birth: "1882-03-01", patronymic: "Петров"),
            Person("h-four-years", "I0003", "Иван", "Образцов", 2, birth: "1876"),
            Person("h-no-year", "I0004", "Иван", "Образцов", 1),
            Person("h-six-years", "I0005", "Иван", "Образцов", 1, birth: "1886"),
            Person("h-woman", "I0006", "Иван", "Образцова", 0, birth: "1880"),
            Person("h-other-father", "I0007", "Иван", "Образцов", 1, birth: "1880", patronymic: "Николаевич"),
            Person("h-other-name", "I0008", "Петр", "Образцов", 1, birth: "1880"));

        var candidates = DuplicatePeople.Classify(person, people);

        Assert.Equal(["h-same-year", "h-two-years", "h-four-years", "h-no-year"], candidates.Select(c => c.Person.Handle));
        Assert.Equal(
            [DuplicatePeople.Level.Strong, DuplicatePeople.Level.Strong, DuplicatePeople.Level.Weak, DuplicatePeople.Level.Weak],
            candidates.Select(c => c.Level));
        Assert.Equal(
            ["same name, born the same year", "same name, born 2 years apart", "same name, born 4 years apart",
             "same name, no birth years to compare"],
            candidates.Select(c => c.Reason));
    }

    [Fact]
    public void Classify_Matches_A_Name_Written_In_The_Other_Alphabet()
    {
        var person = NewPerson("""{ "given": "Jadwiga", "surname": "Dubrowska" }""", gender: 0, birthYear: 1880);
        var people = People(
            Person("h-cyrillic", "I0001", "Ядвига", "Дубровская", 0, birth: "1881"),
            Person("h-male-form", "I0002", "Ядвига", "Dubrovskiy", 0, birth: "1880"),
            Person("h-other", "I0003", "Ядвига", "Дубовская", 0, birth: "1880"));

        var candidates = DuplicatePeople.Classify(person, people);

        Assert.Equal(["h-male-form", "h-cyrillic"], candidates.Select(c => c.Person.Handle));
        Assert.All(candidates, c => Assert.Equal(DuplicatePeople.Level.Strong, c.Level));
    }

    [Fact]
    public void Classify_Makes_A_Child_Of_The_Same_Parents_Without_Years_A_Strong_Match()
    {
        var person = NewPerson("""{ "given": "Анна", "surname": "Образцова" }""", gender: 0, parents: ["h-father"]);
        var people = People(
            Person("h-sister", "I0001", "Анна", "Образцова", 0, fatherHandle: "h-father"),
            Person("h-namesake", "I0002", "Анна", "Образцова", 0, fatherHandle: "h-someone"));

        var candidates = DuplicatePeople.Classify(person, people);

        Assert.Equal(["h-sister", "h-namesake"], candidates.Select(c => c.Person.Handle));
        Assert.Equal([DuplicatePeople.Level.Strong, DuplicatePeople.Level.Weak], candidates.Select(c => c.Level));
        Assert.Equal("same name and parents, no birth years to compare", candidates[0].Reason);
    }

    [Fact]
    public void Classify_Passes_Over_People_Named_In_NotDuplicateOf()
    {
        var person = NewPerson("""{ "given": "Иван", "surname": "Образцов" }""", gender: 1, birthYear: 1880,
            notDuplicateOf: ["i0001", "h-two"]);
        var people = People(
            Person("h-one", "I0001", "Иван", "Образцов", 1, birth: "1880"),
            Person("h-two", "I0002", "Иван", "Образцов", 1, birth: "1880"),
            Person("h-three", "I0003", "Иван", "Образцов", 1, birth: "1880"));

        Assert.Equal(["h-three"], DuplicatePeople.Classify(person, people).Select(c => c.Person.Handle));
    }

    [Theory]
    [InlineData("Мария", "Анна Мария", true)]
    [InlineData("Анна Мария", "Мария Анна", true)]
    [InlineData("Іоаннъ", "Иоанн", true)]
    [InlineData("Robert de", "Gilbert de", false)]
    [InlineData("Walter (Master of)", "John (Sir; of That Ilk)", false)]
    [InlineData("Иван Петрович", "Петр Петрович", false)]
    public void Compare_Needs_The_First_Word_Of_One_Given_Name_In_The_Other(string first, string second, bool matches)
    {
        var match = DuplicatePeople.Compare(
            new DuplicatePeople.Side(Keys($$"""{ "given": "{{first}}", "surname": "Образцов" }"""), 1, 1880, null),
            new DuplicatePeople.Side(Keys($$"""{ "given": "{{second}}", "surname": "Образцов" }"""), 1, 1880, null),
            sameParents: false);

        Assert.Equal(matches, match != null);
    }

    [Theory]
    [InlineData(1880, 1881, 1882, null, false)]
    [InlineData(1882, null, 1880, 1881, false)]
    [InlineData(1880, 1883, 1882, null, true)]
    [InlineData(1880, 1881, null, null, true)]
    [InlineData(1880, null, 1882, null, true)]
    public void Compare_Passes_Over_A_Namesake_Who_Died_Before_The_Other_Was_Born(int? firstBirth, int? firstDeath,
        int? secondBirth, int? secondDeath, bool matches)
    {
        var names = Keys("""{ "given": "Иван", "surname": "Образцов" }""");

        var match = DuplicatePeople.Compare(new DuplicatePeople.Side(names, 1, firstBirth, firstDeath),
            new DuplicatePeople.Side(names, 1, secondBirth, secondDeath), sameParents: true);

        Assert.Equal(matches, match != null);
    }

    [Theory]
    [InlineData(1880, 1920, 1881, 1926, false)]
    [InlineData(null, 1555, null, 1581, false)]
    [InlineData(1880, 1920, 1881, 1925, true)]
    [InlineData(1880, 1920, 1881, null, true)]
    public void Compare_Passes_Over_Namesakes_Who_Died_More_Than_Five_Years_Apart(int? firstBirth, int? firstDeath,
        int? secondBirth, int? secondDeath, bool matches)
    {
        var names = Keys("""{ "given": "Иван", "surname": "Образцов" }""");

        var match = DuplicatePeople.Compare(new DuplicatePeople.Side(names, 1, firstBirth, firstDeath),
            new DuplicatePeople.Side(names, 1, secondBirth, secondDeath), sameParents: true);

        Assert.Equal(matches, match != null);
    }

    [Fact]
    public async Task CreateRecords_Refuses_A_Strong_Match_And_Saves_Nothing()
    {
        var handler = new TreeHandler(PeopleJson(
            Person("h-ivan", "I0012", "Иван", "Образцов", 1, birth: "1881", fatherHandle: "h-petr"),
            Person("h-namesake", "I0013", "Иван", "Образцов", 1)));

        var result = (await CreateIvan(handler)).ReplaceLineEndings("\n");

        Assert.Empty(handler.Posts);
        Assert.StartsWith("---\ntype: Transaction\naction: refused\nduplicates: 1\n---\n", result);
        Assert.Matches(@"people\[0\], Иван \S+ \S+:\n  Possible duplicates \(1\):\n", result);
        Assert.Contains("    • Образцов, Иван (I0012) [handle: h-ivan], b. 1881, " +
                        "child of Образцов, Петр (I0100) — same name, born 1 year apart\n", result);
        Assert.Contains("  Similar people (1):\n    • Образцов, Иван (I0013) [handle: h-namesake]", result);
        Assert.Contains("get_records(objectType: \"person\", identifiers: [\"I0012\"], extended: true)", result);
        Assert.Contains("people[0] notDuplicateOf: [\"I0012\"]", result);
    }

    [Fact]
    public async Task CreateRecords_With_NotDuplicateOf_Creates_And_Lists_The_Others()
    {
        var handler = new TreeHandler(PeopleJson(
            Person("h-ivan", "I0012", "Иван", "Образцов", 1, birth: "1881"),
            Person("h-namesake", "I0013", "Иван", "Образцов", 1)));

        var result = (await CreateIvan(handler, notDuplicateOf: """["I0012"]""")).ReplaceLineEndings("\n");

        Assert.Equal(["/api/objects/"], handler.Posts);
        Assert.Contains("action: created", result);
        Assert.Contains("Similar people already in the tree, possibly the same as a new person:\n", result);
        Assert.Contains("    • Образцов, Иван (I0013) [handle: h-namesake] — same name, no birth years to compare\n", result);
        Assert.Contains("merge_records(objectType: \"person\", keepHandle: \"<their handle>\", " +
                        "duplicateHandle: \"<the new person's handle>\")", result);
        Assert.DoesNotContain("h-ivan", result);
    }

    [Fact]
    public async Task CreateRecords_Without_Namesakes_Says_Nothing_About_Duplicates()
    {
        var handler = new TreeHandler("[]");

        var result = await CreateIvan(handler);

        Assert.Equal(["/api/objects/"], handler.Posts);
        Assert.DoesNotContain("duplicate", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Similar people", result);
        Assert.Single(handler.Requests, p => p.StartsWith("/api/people/?rules=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRecords_Creates_When_The_Search_Fails_And_Says_So()
    {
        var handler = new TreeHandler("""{ "error": "bad rules" }""", searchStatus: HttpStatusCode.UnprocessableEntity);

        var result = await CreateIvan(handler);

        Assert.Equal(["/api/objects/"], handler.Posts);
        Assert.Contains("The duplicate check did not run for people[0]: Gramps Web gave HTTP 422 to the name search. " +
                        "Search the tree for this person before adding more about them.", result);
    }

    [Fact]
    public async Task CreateRecords_Says_When_More_People_Matched_Than_Were_Compared()
    {
        var handler = new TreeHandler(PeopleJson(Person("h-other", "I0012", "Иван", "Образцов", 1, birth: "1870")), total: 240);

        var result = await CreateIvan(handler);

        Assert.Contains("action: created", result);
        Assert.Contains("The duplicate check compared only part of the people with a matching name for " +
                        "people[0] (1 of 240); search the tree for the others.", result);
    }

    /// <summary>create_records with Иван Петрович Образцов, born 1880.</summary>
    private static Task<string> CreateIvan(TreeHandler handler, string? notDuplicateOf = null) =>
        CreateRecordsCall.RunAsync($$"""
            { "people": [{ "primaryName": { "given": "Иван", "surname": "Образцов", "patronymic": "Петрович" },
                           "gender": "Male", "eventRefs": ["e-birth"], "notDuplicateOf": {{notDuplicateOf ?? "null"}} }] }
            """, CreateClient(handler));

    private static bool Matches(string pythonPattern, string text) =>
        Regex.IsMatch(text, pythonPattern, RegexOptions.IgnoreCase);

    private static JsonObject Rules(string path)
    {
        var query = path[(path.IndexOf("rules=", StringComparison.Ordinal) + "rules=".Length)..];
        return JsonNode.Parse(Uri.UnescapeDataString(query[..query.IndexOf('&')]))!.AsObject();
    }

    private static PersonNameMatching.NameKeys Keys(string name) =>
        PersonNameMatching.Keys(JsonSerializer.Deserialize<FlexibleGrampsName>(name)!.Name, null);

    private static DuplicatePeople.NewPerson NewPerson(string name, int gender, int? birthYear = null,
        string[]? parents = null, string[]? notDuplicateOf = null) =>
        new(Keys(name), gender, birthYear, new HashSet<string>(), new HashSet<string>(parents ?? []),
            new HashSet<string>(notDuplicateOf ?? [], StringComparer.OrdinalIgnoreCase));

    private static GrampsPerson[] People(params string[] people) =>
        JsonSerializer.Deserialize<GrampsPerson[]>(PeopleJson(people), GrampsJson.Options)!;

    private static string PeopleJson(params string[] people) => $"[{string.Join(",", people)}]";

    /// <summary>A person as the duplicate check's search returns them, with a profile.</summary>
    internal static string Person(string handle, string grampsId, string first, string surname, int gender,
        string? birth = null, string? patronymic = null, string? fatherHandle = null)
    {
        var surnames = new JsonArray(new JsonObject { ["surname"] = surname, ["primary"] = true });
        if (patronymic != null)
            surnames.Add(new JsonObject { ["surname"] = patronymic, ["primary"] = false, ["origintype"] = "Patronymic" });
        var profile = new JsonObject
        {
            ["handle"] = handle, ["gramps_id"] = grampsId, ["name_display"] = $"{surname}, {first}",
            ["sex"] = gender switch { 0 => "F", 1 => "M", _ => "U" }
        };
        if (birth != null)
            profile["birth"] = new JsonObject { ["type"] = "Birth", ["date"] = birth };
        if (fatherHandle != null)
            profile["primary_parent_family"] = new JsonObject
            {
                ["handle"] = $"f-{fatherHandle}",
                ["father"] = new JsonObject { ["handle"] = fatherHandle, ["gramps_id"] = "I0100", ["name_display"] = "Образцов, Петр" }
            };
        return new JsonObject
        {
            ["handle"] = handle, ["gramps_id"] = grampsId, ["gender"] = gender,
            ["primary_name"] = new JsonObject { ["first_name"] = first, ["surname_list"] = surnames },
            ["alternate_names"] = new JsonArray(),
            ["parent_family_list"] = fatherHandle == null ? new JsonArray() : new JsonArray($"f-{fatherHandle}"),
            ["profile"] = profile
        }.ToJsonString();
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://duplicate-people.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves the vocabularies, a Birth event of 1880 as e-birth and <paramref name="people"/> to the people search;
    /// records the requests and answers a POST as Gramps Web does.
    /// </summary>
    private sealed class TreeHandler(string people, HttpStatusCode searchStatus = HttpStatusCode.OK, int? total = null)
        : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<string> _posts = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public IReadOnlyList<string> Posts => [.. _posts];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Post)
            {
                _posts.Enqueue(path);
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
                return Response(CreateRecordsCall.Changes(body).ToJsonString(), HttpStatusCode.OK);
            }

            if (path.StartsWith("/api/people/?rules=", StringComparison.Ordinal))
            {
                var response = Response(people, searchStatus);
                if (total != null)
                    response.Headers.Add("X-Total-Count", total.ToString());
                return response;
            }

            return path switch
            {
                "/api/types/default/" => Json("""
                    { "event_role_types": ["Primary", "Witness"], "name_origin_types": ["Unknown", "Inherited", "Patronymic"] }
                    """),
                "/api/types/custom/" => Json("{}"),
                "/api/events/e-birth" => Json("""{ "handle": "e-birth", "type": "Birth", "date": { "dateval": [0, 0, 1880, false] } }"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json(string json) => Response(json, HttpStatusCode.OK);

        private static HttpResponseMessage Response(string json, HttpStatusCode status) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
