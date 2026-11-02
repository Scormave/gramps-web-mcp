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
/// The duplicate check of create_person: name folding, the server search, which namesakes are strong or weak
/// matches, and the replies.
/// </summary>
public class DuplicatePeopleTests
{
    [Theory]
    [InlineData("Пугинъ", false, "пугин")]
    [InlineData("Алёна", false, "алена")]
    [InlineData("Ѳеодоръ", false, "феодор")]
    [InlineData("Аввакум", false, "авакум")]
    [InlineData("Łukasz", false, "lukasz")]
    [InlineData("Müller", false, "muler")]
    [InlineData("Мария", true, "марья")]
    [InlineData("Дмитрий", true, "дмитрей")]
    [InlineData("Іоаннъ", true, "иоан")]
    public void Fold_Gives_Spellings_Of_One_Name_One_Form(string text, bool givenName, string expected) =>
        Assert.Equal(expected, PersonNameMatching.Fold(text, givenName));

    [Theory]
    [InlineData("Пугин", "пугин")]
    [InlineData("Пугина", "пугин")]
    [InlineData("Иванова", "иванов")]
    [InlineData("Вознесенский", "вознесенск")]
    [InlineData("Вознесенская", "вознесенск")]
    [InlineData("Толстой", "толст")]
    [InlineData("Толстая", "толст")]
    [InlineData("Kowalski", "kowalsk")]
    [InlineData("Kowalska", "kowalsk")]
    [InlineData("Я", null)]
    public void SurnameStem_Is_Shared_By_Male_And_Female_Forms(string surname, string? expected) =>
        Assert.Equal(expected, PersonNameMatching.SurnameStem(surname));

    [Theory]
    [InlineData("Петрович", "петр")]
    [InlineData("Петровна", "петр")]
    [InlineData("Петров", "петр")]
    [InlineData("Ильич", "иль")]
    [InlineData("Ильинична", "иль")]
    [InlineData("", null)]
    public void PatronymicRoot_Is_The_Fathers_Name(string patronymic, string? expected) =>
        Assert.Equal(expected, PersonNameMatching.PatronymicRoot(patronymic));

    [Theory]
    [InlineData("Пугин", true)]
    [InlineData("ПУГИНА", true)]
    [InlineData("Пугинъ", true)]
    [InlineData("Пуггина", true)]
    [InlineData("Пугинъ Ивановъ", true)]
    [InlineData("de Пугин", true)]
    [InlineData("Пугинский", false)]
    [InlineData("Пугинский Иванов", false)]
    [InlineData("Спугин", false)]
    public void SurnamePattern_Matches_Forms_Of_The_Stem(string surname, bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.SurnamePattern(["пугин"]), surname));

    [Theory]
    [InlineData("Мария", true)]
    [InlineData("Марья", true)]
    [InlineData("Анна Мария", true)]
    [InlineData("Марьяна", false)]
    [InlineData("Анна", false)]
    public void GivenPattern_Matches_A_Word_Of_The_Given_Name(string firstName, bool matches) =>
        Assert.Equal(matches, Matches(PersonNameMatching.GivenPattern(["марья"]), firstName));

    [Fact]
    public void SearchPath_Selects_By_Surname_And_Given_Name_In_One_Request()
    {
        var path = DuplicatePeople.SearchPath(Keys("""{ "given": "Иван", "surname": "Пугин", "patronymic": "Петрович" }"""));

        Assert.NotNull(path);
        Assert.StartsWith("/api/people/?rules=", path);
        Assert.EndsWith("&keys=handle,gramps_id,gender,primary_name,alternate_names,parent_family_list,profile" +
                        "&profile=families&page=1&pagesize=100", path);
        var rules = Rules(path);
        Assert.Equal("and", rules["function"]!.GetValue<string>());
        Assert.Equal(["RegExpName", "RegExpName"], rules["rules"]!.AsArray().Select(r => r!["name"]!.GetValue<string>()));
        var patterns = rules["rules"]!.AsArray().Select(r => r!["values"]![0]!.GetValue<string>()).ToList();
        Assert.True(Matches(patterns[0], "Пугина"));
        Assert.True(Matches(patterns[1], "Анна Иван"));
    }

    [Fact]
    public void SearchPath_Drops_The_Given_Name_When_The_Rules_Get_Too_Long()
    {
        var given = "абвг".Select(c => new string('е', 30) + c).ToHashSet();
        var names = new PersonNameMatching.NameKeys(given, new HashSet<string> { "пугин" }, new HashSet<string>());

        var path = DuplicatePeople.SearchPath(names);

        Assert.NotNull(path);
        var rules = Rules(path)["rules"]!.AsArray();
        Assert.Single(rules);
        Assert.True(Matches(rules[0]!["values"]![0]!.GetValue<string>(), "Пугин"));
    }

    [Theory]
    [InlineData("""{ "given": "Иван" }""")]
    [InlineData("""{ "surname": "Пугин" }""")]
    public void SearchPath_Needs_A_Surname_And_A_Given_Name(string name) =>
        Assert.Null(DuplicatePeople.SearchPath(Keys(name)));

    [Fact]
    public void Classify_Sorts_Namesakes_Into_Strong_And_Weak_And_Drops_The_Others()
    {
        var person = NewPerson("""{ "given": "Иван", "surname": "Пугин", "patronymic": "Петрович" }""", gender: 1,
            birthYear: 1880);
        var people = People(
            Person("h-same-year", "I0001", "Іван", "Пугинъ", 1, birth: "1880"),
            Person("h-two-years", "I0002", "Иван", "Пугин", 1, birth: "1882-03-01", patronymic: "Петров"),
            Person("h-four-years", "I0003", "Иван", "Пугин", 2, birth: "1876"),
            Person("h-no-year", "I0004", "Иван", "Пугин", 1),
            Person("h-six-years", "I0005", "Иван", "Пугин", 1, birth: "1886"),
            Person("h-woman", "I0006", "Иван", "Пугина", 0, birth: "1880"),
            Person("h-other-father", "I0007", "Иван", "Пугин", 1, birth: "1880", patronymic: "Николаевич"),
            Person("h-other-name", "I0008", "Петр", "Пугин", 1, birth: "1880"));

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
    public void Classify_Makes_A_Child_Of_The_Same_Parents_Without_Years_A_Strong_Match()
    {
        var person = NewPerson("""{ "given": "Анна", "surname": "Пугина" }""", gender: 0, parents: ["h-father"]);
        var people = People(
            Person("h-sister", "I0001", "Анна", "Пугина", 0, fatherHandle: "h-father"),
            Person("h-namesake", "I0002", "Анна", "Пугина", 0, fatherHandle: "h-someone"));

        var candidates = DuplicatePeople.Classify(person, people);

        Assert.Equal(["h-sister", "h-namesake"], candidates.Select(c => c.Person.Handle));
        Assert.Equal([DuplicatePeople.Level.Strong, DuplicatePeople.Level.Weak], candidates.Select(c => c.Level));
        Assert.Equal("same name and parents, no birth years to compare", candidates[0].Reason);
    }

    [Fact]
    public void Classify_Passes_Over_People_Named_In_NotDuplicateOf()
    {
        var person = NewPerson("""{ "given": "Иван", "surname": "Пугин" }""", gender: 1, birthYear: 1880,
            notDuplicateOf: ["i0001", "h-two"]);
        var people = People(
            Person("h-one", "I0001", "Иван", "Пугин", 1, birth: "1880"),
            Person("h-two", "I0002", "Иван", "Пугин", 1, birth: "1880"),
            Person("h-three", "I0003", "Иван", "Пугин", 1, birth: "1880"));

        Assert.Equal(["h-three"], DuplicatePeople.Classify(person, people).Select(c => c.Person.Handle));
    }

    [Fact]
    public async Task CreatePerson_Refuses_A_Strong_Match_And_Saves_Nothing()
    {
        var handler = new TreeHandler(PeopleJson(
            Person("h-ivan", "I0012", "Иван", "Пугин", 1, birth: "1881", fatherHandle: "h-petr"),
            Person("h-namesake", "I0013", "Иван", "Пугин", 1)));

        var result = await CreateIvan(handler);

        Assert.Empty(handler.Posts);
        Assert.StartsWith("---\ntype: Person\naction: refused\nname: \"Иван ", result.ReplaceLineEndings("\n"));
        Assert.Contains("\nduplicates: 1\n---\n", result.ReplaceLineEndings("\n"));
        Assert.Matches(@"Did not create Иван \S+ \S+: the tree may already have this person\.", result);
        Assert.Contains("Possible duplicates (1):\n  • Пугин, Иван (I0012) [handle: h-ivan], b. 1881, " +
                        "child of Пугин, Петр (I0100) — same name, born 1 year apart\n", result.ReplaceLineEndings("\n"));
        Assert.Contains("Similar people (1):\n  • Пугин, Иван (I0013) [handle: h-namesake]", result.ReplaceLineEndings("\n"));
        Assert.Contains("get_object(objectType: \"person\", identifier: \"I0012\", extended: true)", result);
        Assert.Contains("notDuplicateOf: [\"I0012\"]", result);
    }

    [Fact]
    public async Task CreatePerson_With_NotDuplicateOf_Creates_And_Lists_The_Others()
    {
        var handler = new TreeHandler(PeopleJson(
            Person("h-ivan", "I0012", "Иван", "Пугин", 1, birth: "1881"),
            Person("h-namesake", "I0013", "Иван", "Пугин", 1)));

        var result = await CreateIvan(handler, notDuplicateOf: """["I0012"]""");

        Assert.Equal(["/api/people/"], handler.Posts);
        Assert.Contains("action: created", result);
        Assert.Contains("Similar people already in the tree (1), possibly the same person:\n" +
                        "  • Пугин, Иван (I0013) [handle: h-namesake] — same name, no birth years to compare\n",
            result.ReplaceLineEndings("\n"));
        Assert.Contains("merge_objects(objectType: \"person\", keepHandle: \"<their handle>\", duplicateHandle: \"h-new\")", result);
        Assert.DoesNotContain("h-ivan", result);
    }

    [Fact]
    public async Task CreatePerson_Without_Namesakes_Says_Nothing_About_Duplicates()
    {
        var handler = new TreeHandler("[]");

        var result = await CreateIvan(handler);

        Assert.Equal(["/api/people/"], handler.Posts);
        Assert.DoesNotContain("duplicate", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Similar people", result);
        Assert.Single(handler.Requests, p => p.StartsWith("/api/people/?rules=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreatePerson_Creates_When_The_Search_Fails_And_Says_So()
    {
        var handler = new TreeHandler("""{ "error": "bad rules" }""", searchStatus: HttpStatusCode.UnprocessableEntity);

        var result = await CreateIvan(handler);

        Assert.Equal(["/api/people/"], handler.Posts);
        Assert.Contains("The duplicate check did not run: Gramps Web gave HTTP 422 to the name search. " +
                        "Search the tree for this person before adding more about them.", result);
    }

    [Fact]
    public async Task CreatePerson_Says_When_More_People_Matched_Than_Were_Compared()
    {
        var handler = new TreeHandler(PeopleJson(Person("h-other", "I0012", "Иван", "Пугин", 1, birth: "1870")), total: 240);

        var result = await CreateIvan(handler);

        Assert.Contains("action: created", result);
        Assert.Contains("The duplicate check compared the first 1 of 240 people with a matching name; " +
                        "search the tree for the others.", result);
    }

    /// <summary>create_person for Иван Петрович Пугин, born 1880.</summary>
    private static Task<string> CreateIvan(TreeHandler handler, string? notDuplicateOf = null) =>
        PersonTools.CreatePerson(
            JsonSerializer.Deserialize<FlexibleGrampsName>(
                """{ "given": "Иван", "surname": "Пугин", "patronymic": "Петрович" }""")!,
            gender: "Male",
            eventRefs: JsonSerializer.Deserialize<FlexibleEventRefList>("""["e-birth"]"""),
            notDuplicateOf: notDuplicateOf == null ? null : JsonSerializer.Deserialize<FlexibleHandleList>(notDuplicateOf),
            client: CreateClient(handler));

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
                ["father"] = new JsonObject { ["handle"] = fatherHandle, ["gramps_id"] = "I0100", ["name_display"] = "Пугин, Петр" }
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
    /// records the requests and answers a POST with the new person h-new.
    /// </summary>
    private sealed class TreeHandler(string people, HttpStatusCode searchStatus = HttpStatusCode.OK, int? total = null)
        : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<string> _posts = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        public IReadOnlyList<string> Posts => [.. _posts];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (request.Method == HttpMethod.Post)
            {
                _posts.Enqueue(path);
                return Json("""[{ "_class": "Person", "type": "add", "old": null, "new": { "_class": "Person", "handle": "h-new", "gramps_id": "I0001" } }]""");
            }

            if (path.StartsWith("/api/people/?rules=", StringComparison.Ordinal))
            {
                var response = Response(people, searchStatus);
                if (total != null)
                    response.Headers.Add("X-Total-Count", total.ToString());
                return Task.FromResult(response);
            }

            return path switch
            {
                "/api/types/default/" => Json("""
                    { "event_role_types": ["Primary", "Witness"], "name_origin_types": ["Unknown", "Inherited", "Patronymic"] }
                    """),
                "/api/types/custom/" => Json("{}"),
                "/api/events/e-birth" => Json("""{ "handle": "e-birth", "type": "Birth", "date": { "dateval": [0, 0, 1880, false] } }"""),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            };
        }

        private static Task<HttpResponseMessage> Json(string json) => Task.FromResult(Response(json, HttpStatusCode.OK));

        private static HttpResponseMessage Response(string json, HttpStatusCode status) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
