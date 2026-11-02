using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GrampsWeb.Mcp.Client;
using GrampsWeb.Mcp.Config;
using GrampsWeb.Mcp.Models;
using GrampsWeb.Mcp.Serialization;
using GrampsWeb.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace GrampsWeb.Mcp.Tests.UnitTests;

/// <summary>find_duplicates: which pairs of the tree are compared and listed, the reply, and the paged read.</summary>
public class DuplicateToolsTests
{
    [Fact]
    public void Scan_Pairs_Spellings_And_Lists_Possible_Duplicates_First()
    {
        var people = People(
            Person("h1", "I0001", "Иван", "Образцов", birth: "1880"),
            Person("h2", "I0002", "Іван", "Образцовъ", birth: "1881"),
            Person("h3", "I0003", "Иван", "Образцов", birth: "1884"),
            Person("h4", "I0004", "Анна", "Образцова", gender: 0, birth: "1880"),
            Person("h5", "I0005", "Анна", "Образцов", gender: 0),
            Person("h6", "I0006", "Иван", "Сидоров", birth: "1880"));

        var scan = DuplicateTools.Scan(people);

        Assert.Equal([("h1", "h2"), ("h2", "h3"), ("h1", "h3"), ("h4", "h5")], Handles(scan));
        Assert.Equal(
            ["same name, born 1 year apart", "same name, born 3 years apart", "same name, born 4 years apart",
             "same name, no birth years to compare"],
            scan.Pairs.Select(p => p.Match.Reason));
        Assert.Equal(1, scan.Strong);
        Assert.Equal((6, 0), (scan.Compared, scan.Skipped));
    }

    [Fact]
    public void Scan_Never_Pairs_Spouses_Or_A_Person_With_Their_Ancestor()
    {
        var people = People(
            Person("h-father", "I0001", "Иван", "Образцов", birth: "1850", families: ["f1"]),
            Person("h-son", "I0002", "Иван", "Образцов", birth: "1852", parentFamily: "f1"),
            Person("h-husband", "I0003", "Саша", "Образцов", gender: 2, birth: "1850", families: ["f2"]),
            Person("h-wife", "I0004", "Саша", "Образцова", gender: 2, birth: "1850", families: ["f2"]),
            Person("h-other", "I0005", "Саша", "Образцов", gender: 2, birth: "1851"),
            Person("h-grandfather", "I0006", "Федор", "Образцов", families: ["f3"]),
            Person("h-unnamed", "I0007", "Петр", parentFamily: "f3", families: ["f4"]),
            Person("h-grandson", "I0008", "Федор", "Образцов", parentFamily: "f4"));

        var scan = DuplicateTools.Scan(people);

        Assert.Equal([("h-husband", "h-other"), ("h-wife", "h-other")], Handles(scan));
        Assert.Equal((7, 1), (scan.Compared, scan.Skipped));
    }

    [Fact]
    public void Scan_Pairs_By_The_First_Given_Name_Not_By_Any_Word()
    {
        var people = People(
            Person("h1", "I0001", "Анна Мария", "Образцова", gender: 0, birth: "1880"),
            Person("h2", "I0002", "Мария", "Образцова", gender: 0, birth: "1881"),
            Person("h3", "I0003", "Robert de", "Clare", birth: "1100"),
            Person("h4", "I0004", "Gilbert de", "Clare", birth: "1100"),
            Person("h5", "I0005", "Иван Петрович", "Сидоров", birth: "1880"),
            Person("h6", "I0006", "Петр Петрович", "Сидоров", birth: "1880"));

        Assert.Equal([("h1", "h2")], Handles(DuplicateTools.Scan(people)));
    }

    [Fact]
    public void Scan_Passes_Over_A_Namesake_Who_Died_Before_The_Other_Was_Born()
    {
        var people = People(
            Person("h-parent", "I0001", "Петр", "Образцов", birth: "1850", families: ["f1"]),
            Person("h-first", "I0002", "Иван", "Образцов", birth: "1880", death: "1881", parentFamily: "f1"),
            Person("h-second", "I0003", "Иван", "Образцов", birth: "1883", parentFamily: "f1"),
            Person("h-parent2", "I0004", "Петр", "Сидоров", birth: "1850", families: ["f2"]),
            Person("h-third", "I0005", "Иван", "Сидоров", birth: "1880", parentFamily: "f2"),
            Person("h-fourth", "I0006", "Иван", "Сидоров", birth: "1883", parentFamily: "f2"));

        var scan = DuplicateTools.Scan(people);

        Assert.Equal([("h-third", "h-fourth")], Handles(scan));
        Assert.Equal("same name and parents, born 3 years apart", scan.Pairs[0].Match.Reason);
    }

    [Fact]
    public void Scan_Pairs_People_Without_A_Surname_Only_When_Years_Or_Parents_Compare()
    {
        var people = People(
            Person("h1", "I0001", "Иван", patronymic: "Петров", birth: "1850"),
            Person("h2", "I0002", "Иван", patronymic: "Петрович", birth: "1851"),
            Person("h3", "I0003", "Иван", patronymic: "Петров"),
            Person("h4", "I0004", "Иван", patronymic: "Петров", parentFamily: "f1"),
            Person("h5", "I0005", "Иван", patronymic: "Петров", parentFamily: "f1"),
            Person("h6", "I0006", "Иван"),
            Person("h7", "I0007", "", "Образцов"));

        var scan = DuplicateTools.Scan(people);

        Assert.Equal([("h1", "h2"), ("h4", "h5")], Handles(scan));
        Assert.Equal(
            ["same given name and patronymic, born 1 year apart",
             "same given name, patronymic and parents, no birth years to compare"],
            scan.Pairs.Select(p => p.Match.Reason));
        Assert.Equal(2, scan.Strong);
        Assert.Equal((5, 2), (scan.Compared, scan.Skipped));
    }

    [Fact]
    public void Scan_Keeps_The_Pairs_With_The_Person_Or_The_Surname()
    {
        var people = People(
            Person("h1", "I0001", "Иван", "Образцов", birth: "1880"),
            Person("h2", "I0002", "Иван", "Образцов", birth: "1881"),
            Person("h3", "I0003", "Иван", "Образцов", birth: "1890"),
            Person("h4", "I0004", "Иван", "Сидоров", birth: "1880"),
            Person("h5", "I0005", "Иван", "Сидоров", birth: "1880"),
            Person("h6", "I0006", "Анна", "Образцова", gender: 0, alternate: ("Анна", "Сидорова")),
            Person("h7", "I0007", "Анна", "Сидорова", gender: 0));

        Assert.Equal([("h1", "h2")], Handles(DuplicateTools.Scan(people, personHandle: "h1")));
        Assert.Equal([("h4", "h5"), ("h6", "h7")], Handles(DuplicateTools.Scan(people, surnameStem: "сидоров")));
    }

    [Fact]
    public void Format_Lists_The_Pairs_With_Their_Parents_And_The_Next_Steps()
    {
        var people = Family();
        var scan = DuplicateTools.Scan(people);

        var text = DuplicateTools.Format(scan, people, people.Length, null, null, limit: 2, offset: 0);

        Assert.StartsWith("POSSIBLE DUPLICATE PEOPLE\n" + new string('=', 60), text.ReplaceLineEndings("\n"));
        Assert.Contains("Compared 7 people.", text);
        Assert.Contains("2 possible duplicates and 1 similar pair; showing 1–2.", text);
        Assert.Contains("Possible duplicates (2):", text);
        Assert.Contains("1. Same name and parents, born the same year:", text);
        Assert.Contains("   • Образцов, Иван (I0003) [handle: h3], b. 1880, child of Образцов, Петр (I0001) and Образцова, Анна (I0002)",
            text);
        Assert.DoesNotContain("Similar pairs", text);
        Assert.Contains("get_object(objectType: \"person\", identifier: \"I0003\", extended: true), and the same for \"I0004\".",
            text);
        Assert.Contains("merge_objects(objectType: \"person\"", text);
        Assert.Contains("More pairs: find_duplicates(offset: 2).", text);
    }

    [Fact]
    public void Format_Numbers_Pairs_Across_Pages_And_Says_When_The_Offset_Is_Past_The_End()
    {
        var people = Family();
        var scan = DuplicateTools.Scan(people);
        var person = people.Single(p => p.Handle == "h3");

        var last = DuplicateTools.Format(scan, people, people.Length, person, "Образцов", limit: 5, offset: 2);
        var past = DuplicateTools.Format(scan, people, people.Length, null, null, limit: 5, offset: 10);

        Assert.Contains("Only pairs with Образцов, Иван (I0003) [handle: h3], b. 1880.", last);
        Assert.Contains("Only pairs where one of the people has the surname Образцов, in any spelling or form.", last);
        Assert.Contains("showing 3–3.", last);
        Assert.Contains("Similar pairs (1):\n3. Same name, born 4 years apart:", last.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("More pairs", last);
        Assert.Contains("No pairs from 11: call find_duplicates with a smaller offset.", past);
        Assert.DoesNotContain("Next steps", past);
    }

    [Fact]
    public void Format_Says_When_There_Are_No_Pairs_And_When_The_Tree_Was_Read_In_Part()
    {
        var people = People(Person("h1", "I0001", "Иван", "Образцов"), Person("h2", "I0002", "", "Образцов"));

        var text = DuplicateTools.Format(DuplicateTools.Scan(people), people, 30000, null, null, 20, 0);

        Assert.Contains("Compared 1 person; left out 1 without a given name, or with neither a surname nor a patronymic.", text);
        Assert.Contains("Read the first 2 of 30000 people; pairs with the others are not shown.", text);
        Assert.Contains("No possible duplicates or similar pairs.", text);
    }

    [Fact]
    public async Task FindDuplicates_Reads_The_Tree_Page_By_Page()
    {
        var unnamed = Enumerable.Range(1, DuplicateTools.PageSize).Select(i => Person($"h-unnamed-{i}", $"I{i:D4}", ""));
        var handler = new PeopleHandler(
            total: DuplicateTools.PageSize + 2,
            PeopleJson([.. unnamed]),
            PeopleJson(Person("h-a", "I1001", "Иван", "Образцов", birth: "1880"),
                Person("h-b", "I1002", "Иван", "Образцова", birth: "1880")));

        var text = await DuplicateTools.FindDuplicatesAsync(null, null, 20, 0, CreateClient(handler));

        Assert.Equal(
            ["page=1&pagesize=500", "page=2&pagesize=500"],
            handler.Requests.Select(r => r[(r.IndexOf("page=", StringComparison.Ordinal))..]));
        Assert.All(handler.Requests, r => Assert.StartsWith(
            "/api/people/?keys=handle,gramps_id,gender,primary_name,alternate_names,parent_family_list,family_list,profile&profile=self&",
            r));
        Assert.Contains("Compared 2 people; left out 500 without a given name", text);
        Assert.Contains("1. Same name, born the same year:", text);
    }

    [Fact]
    public async Task FindDuplicates_Resolves_A_Gramps_ID_And_Keeps_Its_Pairs()
    {
        var handler = new PeopleHandler(
            total: 3,
            PeopleJson(Person("h-a", "I0001", "Иван", "Образцов", birth: "1880"),
                Person("h-b", "I0002", "Иван", "Образцов", birth: "1880"),
                Person("h-c", "I0003", "Анна", "Образцова", gender: 0, birth: "1880")));

        var text = await DuplicateTools.FindDuplicatesAsync("I0002", null, 20, 0, CreateClient(handler));

        Assert.Contains(handler.Requests, r => r.StartsWith("/api/people/?gramps_id=I0002", StringComparison.Ordinal));
        Assert.Contains("Only pairs with Образцов, Иван (I0002) [handle: h-b]", text);
        Assert.Contains("1 possible duplicate and 0 similar pairs; showing 1–1.", text);
    }

    [Fact]
    public async Task FindDuplicates_Refuses_A_Short_Surname_And_An_Unknown_Person()
    {
        var handler = new PeopleHandler(total: 1, PeopleJson(Person("h-a", "I0001", "Иван", "Образцов")));

        var shortSurname = await Assert.ThrowsAsync<McpException>(() =>
            DuplicateTools.FindDuplicatesAsync(null, "Я", 20, 0, CreateClient(handler)));
        var unknown = await Assert.ThrowsAsync<McpException>(() =>
            DuplicateTools.FindDuplicatesAsync("h-missing", null, 20, 0, CreateClient(handler)));

        Assert.Contains("surname needs at least two letters", shortSurname.Message);
        Assert.Contains("h-missing", unknown.Message);
    }

    /// <summary>
    /// Петр and Анна Образцовы with two sons Иван born the same year and a daughter Мария; another Мария born a year
    /// later and another Петр born four years later.
    /// </summary>
    private static GrampsPerson[] Family() => People(
        Person("h1", "I0001", "Петр", "Образцов", birth: "1850", families: ["f1"]),
        Person("h2", "I0002", "Анна", "Образцова", gender: 0, birth: "1855", families: ["f1"]),
        Person("h3", "I0003", "Иван", "Образцов", birth: "1880", parentFamily: "f1"),
        Person("h4", "I0004", "Иван", "Образцов", birth: "1880", parentFamily: "f1"),
        Person("h5", "I0005", "Мария", "Образцова", gender: 0, birth: "1881", parentFamily: "f1"),
        Person("h6", "I0006", "Марья", "Образцова", gender: 0, birth: "1882"),
        Person("h7", "I0007", "Петр", "Образцов", birth: "1854", death: "1920"));

    private static (string, string)[] Handles(DuplicateTools.ScanResult scan) =>
        [.. scan.Pairs.Select(p => (p.First.Handle!, p.Second.Handle!))];

    private static GrampsPerson[] People(params string[] people) =>
        JsonSerializer.Deserialize<GrampsPerson[]>(PeopleJson(people), GrampsJson.Options)!;

    private static string PeopleJson(params string[] people) => $"[{string.Join(",", people)}]";

    /// <summary>A person as the scan reads them: names, gender, family links and a profile with birth and death.</summary>
    private static string Person(string handle, string grampsId, string first, string? surname = null, int gender = 1,
        string? birth = null, string? death = null, string? patronymic = null, string? parentFamily = null,
        string[]? families = null, (string First, string Surname)? alternate = null)
    {
        var surnames = new JsonArray();
        if (surname != null)
            surnames.Add(new JsonObject { ["surname"] = surname, ["primary"] = true });
        if (patronymic != null)
            surnames.Add(new JsonObject { ["surname"] = patronymic, ["primary"] = surname == null, ["origintype"] = "Patronymic" });
        var profile = new JsonObject
        {
            ["handle"] = handle, ["gramps_id"] = grampsId, ["name_display"] = $"{surname ?? patronymic}, {first}",
            ["sex"] = gender switch { 0 => "F", 1 => "M", _ => "U" }
        };
        if (birth != null)
            profile["birth"] = new JsonObject { ["type"] = "Birth", ["date"] = birth };
        if (death != null)
            profile["death"] = new JsonObject { ["type"] = "Death", ["date"] = death };
        var alternates = new JsonArray();
        if (alternate is var (altFirst, altSurname))
            alternates.Add(new JsonObject
            {
                ["first_name"] = altFirst,
                ["surname_list"] = new JsonArray(new JsonObject { ["surname"] = altSurname, ["primary"] = true })
            });
        return new JsonObject
        {
            ["handle"] = handle, ["gramps_id"] = grampsId, ["gender"] = gender,
            ["primary_name"] = new JsonObject { ["first_name"] = first, ["surname_list"] = surnames },
            ["alternate_names"] = alternates,
            ["parent_family_list"] = parentFamily == null ? new JsonArray() : new JsonArray(parentFamily),
            ["family_list"] = new JsonArray([.. (families ?? []).Select(f => (JsonNode)f)]),
            ["profile"] = profile
        }.ToJsonString();
    }

    private static GrampsApiClient CreateClient(HttpMessageHandler handler)
    {
        var config = new GrampsConfig("https://find-duplicates.test", "user", "pass", "tree");
        var http = new HttpClient(handler);
        var tokens = new GrampsAuthTokenProvider(http, config, NullLogger<GrampsAuthTokenProvider>.Instance);
        return new GrampsApiClient(http, config, NullLogger<GrampsApiClient>.Instance, tokens);
    }

    /// <summary>
    /// Serves <paramref name="pages"/> to the scan with <paramref name="total"/> as X-Total-Count, and a Gramps ID
    /// lookup from the first page; records the requests.
    /// </summary>
    private sealed class PeopleHandler(int total, params string[] pages) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.PathAndQuery);
            if (path.StartsWith("/api/token/", StringComparison.Ordinal))
                return Json("""{"access_token":"token","refresh_token":"refresh","expires_in":900}""");

            _requests.Enqueue(path);
            if (path.StartsWith("/api/people/?gramps_id=", StringComparison.Ordinal))
            {
                var id = path["/api/people/?gramps_id=".Length..path.IndexOf('&')];
                var found = JsonNode.Parse(pages[0])!.AsArray().Where(p => (string?)p!["gramps_id"] == id);
                return Json(new JsonArray([.. found.Select(p => p!.DeepClone())]).ToJsonString());
            }

            if (path.StartsWith("/api/people/?keys=", StringComparison.Ordinal))
            {
                var page = int.Parse(path[(path.IndexOf("&page=", StringComparison.Ordinal) + "&page=".Length)..].Split('&')[0]);
                var response = Response(page <= pages.Length ? pages[page - 1] : "[]");
                response.Headers.Add("X-Total-Count", total.ToString());
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json) => Task.FromResult(Response(json));

        private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
